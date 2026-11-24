#!/usr/bin/env bash
#
# A grid operator and three local controllers, end to end, over OpenADR 3 -
# over HTTP, over the WebSocket port of OpenADR and over SSH.
#
# GridOperatorCLI starts, gets a grid connection point and three local
# controllers behind it - over its JSON API, as root -, lets the second connect
# over the WebSocket port in the binding of messages and the third over SSH in
# the REST binding, and sends one limit to all three. Three LocalControllerCLIs
# start, each with what the grid operator said once: the first with its client
# secret, the other two with the URL and the password of their connection,
# which they are given over their own JSON API, as root. It is green when each
# of them read the limit and the grid operator has a report from each: sign-in,
# events, the limit and the report, across the network between two programs
# that only share the schema - three times, one way each.
#
#   GRIDOPERATOR_DLL     GridOperatorCLI.dll   (default: this repository's Release build)
#   LOCALCONTROLLER_DLL  LocalControllerCLI.dll (default: ../LocalControllerCLI's Release build)
#
# Ports 18129/22129 for the grid operator and 18130 for its WebSocket port of
# OpenADR; 18127/22127, 18131/22131 and 18132/22132 for the local controllers -
# the first two pairs the ones the two start tests in CI use.
#
# No jq and no python: the Debian container of the nightly has neither, so the
# few values needed are cut out of the JSON with sed - which the JSON APIs of
# both write on one line.

set -u

here="$(cd "$(dirname "$0")/.." && pwd)"
go_dll="${GRIDOPERATOR_DLL:-$here/GridOperatorCLI/bin/Release/net11.0/GridOperatorCLI.dll}"
lc_dll="${LOCALCONTROLLER_DLL:-$here/../LocalControllerCLI/LocalControllerCLI/bin/Release/net11.0/LocalControllerCLI.dll}"

for dll in "$go_dll" "$lc_dll"; do
    if [ ! -f "$dll" ]; then
        echo "There is no $dll: build it first."
        exit 2
    fi
done

tmp="$(mktemp -d)"
mkdir -p "$tmp/go"

go_url="http://127.0.0.1:18129"
go_pid=""
lc_pids=""

# The three local controllers: their VEN name, how they connect, their ports.
vens="lc-http lc-ws lc-ssh"

http_port_of() { case "$1" in lc-http) echo 18127;; lc-ws) echo 18131;; lc-ssh) echo 18132;; esac; }
ssh_port_of()  { case "$1" in lc-http) echo 22127;; lc-ws) echo 22131;; lc-ssh) echo 22132;; esac; }
pool_of()      { case "$1" in lc-http) echo 'DE*GEF*P1';; lc-ws) echo 'DE*GEF*P2';; lc-ssh) echo 'DE*GEF*P3';; esac; }
way_of()       { case "$1" in lc-http) echo "HTTP";; lc-ws) echo "the WebSocket port, messages";; lc-ssh) echo "SSH, REST";; esac; }

stop() {
    for pid in $lc_pids $go_pid; do kill "$pid" 2>/dev/null; done
    for pid in $lc_pids $go_pid; do wait "$pid" 2>/dev/null; done
    return 0
}

fail() {
    echo "$1"
    stop
    echo
    echo "What the grid operator said, without the first-start password:"
    grep -v -i password "$tmp/go/console.log" 2>/dev/null | grep -v -i "nts" | tail -60 || true
    for ven in $vens; do
        [ -f "$tmp/$ven/console.log" ] || continue
        echo
        echo "What the local controller $ven said, without the first-start password:"
        grep -v -i password "$tmp/$ven/console.log" 2>/dev/null | grep -v -i "nts" | tail -40 || true
    done
    exit 1
}

# A request to a JSON API as root: the status code, and the body in the given
# answer file.
#   request <base URL> <password> <answer file> <method> <path> [<body>]
request() {
    local base="$1" pw="$2" answer="$3" method="$4" path="$5" body="${6:-}"
    local code
    if [ -n "$body" ]; then
        code="$(curl -sS -m 15 -u "root:$pw" -X "$method" -H 'Content-Type: application/json' -d "$body" \
                     -o "$answer" -w '%{http_code}' "$base$path" 2>/dev/null || true)"
    else
        code="$(curl -sS -m 15 -u "root:$pw" -X "$method" \
                     -o "$answer" -w '%{http_code}' "$base$path" 2>/dev/null || true)"
    fi
    echo "${code:-000}"
}

# A request to the grid operator's JSON API as root.
api() { request "$go_url" "$password" "$tmp/answer.json" "$@"; }

# The string value of the first "Name":"..." in a JSON file.
value() {
    grep -o "\"$1\" *: *\"[^\"]*\"" "$2" 2>/dev/null | head -n 1 | sed "s/.*: *\"\\(.*\\)\"/\\1/"
}

# The number value of the first "Name":123 in a JSON file.
number() {
    grep -o "\"$1\" *: *[0-9][0-9]*" "$2" 2>/dev/null | head -n 1 | sed "s/.*: *//"
}

# Of the local controllers the grid operator lists, the one of the given VEN
# name, alone in a file of its own: so that value and number find its fields.
one_of() {
    sed 's/{"venID"/\n{"venID"/g' "$1" | grep "\"venName\":\"$2\"" > "$3"
}

# The password said once in a banner: "│  password  <it>".
banner_password() {
    local pw=""
    for _ in $(seq 1 40); do
        pw="$(grep -a 'password ' "$1" 2>/dev/null | head -n 1 | awk '{ print $NF }')"
        [ -n "$pw" ] && break
        sleep 0.5
    done
    echo "$pw"
}

# Wait for a web interface to answer: its status code, 000 for none.
wait_up() {
    local url="$1" pid="$2" up=000
    for _ in $(seq 1 60); do
        up="$(curl -sS -m 5 -o /dev/null -w '%{http_code}' "$url/" 2>/dev/null || true)"
        up="${up:-000}"
        [ "$up" != 000 ] && break
        kill -0 "$pid" 2>/dev/null || break
        sleep 1
    done
    echo "$up"
}

#region The grid operator

cat > "$tmp/go/configuration.json" <<'EOF'
{
  "gridOperator":     { "@id": "DE*SWJ", "name": "Netze Beispielstadt" },
  "webSocketServer":  { "enabled": true, "address": "127.0.0.1", "port": 18130,
                        "clientAuthentication": [ "httpAuthWithoutTLS" ],
                        "ssh": { "enabled": true } }
}
EOF

dotnet "$go_dll" \
    --port          18129 \
    --ssh-port      22129 \
    --accounts      "$tmp/go/accounts" \
    --config        "$tmp/go/configuration.json" \
    --certificates  "$tmp/go/certificates" \
    --log-file      "$tmp/go/logs" \
    < /dev/null > "$tmp/go/console.log" 2>&1 &
go_pid=$!

up="$(wait_up "$go_url" "$go_pid")"
[ "$up" = 200 ] || fail "The grid operator did not start ($up)."

password="$(banner_password "$tmp/go/console.log")"
[ -n "$password" ] || fail "The grid operator showed no password for root."

code="$(api POST /api/v1/gridConnectionPoints '{
    "@id": "GCP-1", "name": { "de": "Netzanschluss GCP-1" },
    "voltageLevel": "LowVoltage", "connectionType": "AC_ThreePhases",
    "nominalVoltage": 400, "nominalFrequency": 50,
    "contractedImportPower": 110000, "contractedImportApparentPower": 120000,
    "marketLocationIds": [ "50123456789" ] }')"
[ "$code" = 201 ] || [ "$code" = 200 ] || fail "The grid connection point was not added ($code): $(head -c 500 "$tmp/answer.json")"

for ven in $vens; do
    code="$(api POST /api/v1/openADR/localControllers "{ \"venName\": \"$ven\", \"chargingPoolId\": \"$(pool_of "$ven")\", \"gridConnectionPointId\": \"GCP-1\" }")"
    [ "$code" = 201 ] || fail "The local controller $ven was not added ($code): $(head -c 500 "$tmp/answer.json")"
    cp "$tmp/answer.json" "$tmp/added-$ven.json"
done

# The first signs in over HTTP, with its client secret.
http_vtn="$(value vtnURL "$tmp/added-lc-http.json")"
http_secret="$(value clientSecret "$tmp/added-lc-http.json")"
[ -n "$http_vtn" ] && [ -n "$http_secret" ] || fail "Adding lc-http said no VTN URL or no secret."

# The second connects over the WebSocket port, the third over SSH.
code="$(api POST /api/v1/openADR/localControllers/lc-ws/connections '{ "transport": "ws", "binding": "messages" }')"
[ "$code" = 201 ] || fail "lc-ws was not let connect over the WebSocket port ($code): $(head -c 500 "$tmp/answer.json")"
cp "$tmp/answer.json" "$tmp/connected-lc-ws.json"

code="$(api POST /api/v1/openADR/localControllers/lc-ssh/connections '{ "transport": "ssh", "binding": "rest" }')"
[ "$code" = 201 ] || fail "lc-ssh was not let connect over SSH ($code): $(head -c 500 "$tmp/answer.json")"
cp "$tmp/answer.json" "$tmp/connected-lc-ssh.json"

for ven in lc-ws lc-ssh; do
    [ -n "$(value vtnURL "$tmp/connected-$ven.json")" ] && [ -n "$(value password "$tmp/connected-$ven.json")" ] ||
        fail "Letting $ven connect said no URL or no password."
done
[ -n "$(value sshHostKey "$tmp/connected-lc-ssh.json")" ] || fail "Letting lc-ssh connect over SSH said no host key."

# One limit, for every local controller behind the point.
code="$(api POST /api/v1/openADR/limits '{ "kind": "IMPORT_CAPACITY_LIMIT", "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }')"
[ "$code" = 201 ] || [ "$code" = 200 ] || fail "The limit was not sent ($code): $(head -c 500 "$tmp/answer.json")"

echo "grid operator     up, root signed in, GCP-1, $vens and a limit of 44 kW for all three added"
echo "                  lc-http   over $http_vtn"
echo "                  lc-ws     over $(value vtnURL "$tmp/connected-lc-ws.json"), messages"
echo "                  lc-ssh    over $(value vtnURL "$tmp/connected-lc-ssh.json"), REST"

#endregion

#region The local controllers

for ven in $vens; do

    dir="$tmp/$ven"
    mkdir -p "$dir"

    if [ "$ven" = lc-http ]; then
        cat > "$dir/configuration.json" <<EOF
{ "openADR": { "enabled": true, "vtnURL": "$http_vtn", "clientID": "lc-http", "pollEvery": 5 } }
EOF
        # The secret in its own file beside the configuration, as "openADR
        # import" writes it - never a switch of the program.
        printf '{ "clientSecret": "%s" }\n' "$http_secret" > "$dir/openadr-credentials.json"
        chmod 600 "$dir/openadr-credentials.json" 2>/dev/null || true
    else
        # Through its connection to the grid operator: the URL of the
        # connection is its "vtnURL".
        cat > "$dir/configuration.json" <<EOF
{ "openADR": { "enabled": true, "vtnURL": "$(value vtnURL "$tmp/connected-$ven.json")", "pollEvery": 5 } }
EOF
    fi

    dotnet "$lc_dll" \
        --port      "$(http_port_of "$ven")" \
        --ssh-port  "$(ssh_port_of "$ven")" \
        --accounts  "$dir/accounts" \
        --config    "$dir/configuration.json" \
        --log-file  "$dir/logs" \
        < /dev/null > "$dir/console.log" 2>&1 &
    lc_pids="$lc_pids $!"
    echo $! > "$dir/pid"

done

# The two that connect get their connection over their own JSON API, as root:
# the password as the credentials of the login, the connection with its URL,
# its binding and - over SSH - the host key of the grid operator.
for ven in lc-ws lc-ssh; do

    dir="$tmp/$ven"
    lc_url="http://127.0.0.1:$(http_port_of "$ven")"

    up="$(wait_up "$lc_url" "$(cat "$dir/pid")")"
    [ "$up" = 200 ] || [ "$up" = 302 ] || fail "The local controller $ven did not start ($up)."

    lc_password="$(banner_password "$dir/console.log")"
    [ -n "$lc_password" ] || fail "The local controller $ven showed no password for root."

    connected="$tmp/connected-$ven.json"

    code="$(request "$lc_url" "$lc_password" "$dir/answer.json" POST /api/v1/configuration/authentications \
                    "{ \"kind\": \"basic\", \"login\": \"$(value login "$connected")\", \"secret\": \"$(value password "$connected")\", \"description\": { \"en\": \"The grid operator\" } }")"
    [ "$code" = 201 ] || fail "$ven took no credentials ($code): $(head -c 500 "$dir/answer.json")"

    authentication="$(value id "$dir/answer.json")"

    host_key=""
    [ "$ven" = lc-ssh ] && host_key=", \"sshHostKey\": \"$(value sshHostKey "$connected")\""

    code="$(request "$lc_url" "$lc_password" "$dir/answer.json" POST /api/v1/configuration/connections \
                    "{ \"description\": { \"en\": \"The grid operator\" }, \"url\": \"$(value vtnURL "$connected")\", \"connectionType\": \"GridOperator\", \"autoConnect\": true, \"authenticationId\": \"$authentication\", \"openADRBinding\": \"$(value openADRBinding "$connected")\"$host_key }")"
    [ "$code" = 201 ] || fail "$ven took no connection to the grid operator ($code): $(head -c 500 "$dir/answer.json")"

done

#endregion

#region The limit read, and a report on it from each

done_all=0
for _ in $(seq 1 120); do

    for ven in $vens; do
        kill -0 "$(cat "$tmp/$ven/pid")" 2>/dev/null || fail "The local controller $ven is no longer running."
    done

    code="$(api GET /api/v1/openADR/localControllers)"
    if [ "$code" = 200 ]; then
        done_all=1
        for ven in $vens; do
            one_of "$tmp/answer.json" "$ven" "$tmp/listed-$ven.json"
            reports="$(number reports "$tmp/listed-$ven.json")"
            [ "${reports:-0}" -ge 1 ] || done_all=0
        done
        [ "$done_all" = 1 ] && break
    fi

    sleep 1

done

failed=""

for ven in $vens; do

    reports="$(number reports "$tmp/listed-$ven.json")"
    limit_read=0
    grep -a -r -q "The grid operator sent a limit" "$tmp/$ven/console.log" "$tmp/$ven/logs" 2>/dev/null && limit_read=1

    printf 'local controller  %-7s over %-29s limit read: %-3s  reports at the grid operator: %s\n' \
           "$ven" "$(way_of "$ven")" "$([ "$limit_read" = 1 ] && echo yes || echo no)" "${reports:-0}"

    [ "$limit_read" = 1 ] && [ "${reports:-0}" -ge 1 ] || failed="$failed $ven"

done

[ -z "$failed" ] || fail "Not end to end:$failed."

#endregion

stop
echo "End to end: the limit went from the grid operator to each local controller - over HTTP, the WebSocket port and SSH - and each report came back."
