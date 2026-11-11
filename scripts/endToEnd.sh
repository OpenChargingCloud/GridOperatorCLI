#!/usr/bin/env bash
#
# A grid operator and a local controller, end to end, over OpenADR 3.
#
# GridOperatorCLI starts, gets a grid connection point, a local controller
# behind it and a limit for it - over its JSON API, as root - and then a
# LocalControllerCLI starts with the credentials the grid operator said once.
# It is green when the local controller read the limit and the grid operator
# has its report on it: sign-in, token, events, the limit and the report, each
# across the network between two programs that only share the schema.
#
#   GRIDOPERATOR_DLL     GridOperatorCLI.dll   (default: this repository's Release build)
#   LOCALCONTROLLER_DLL  LocalControllerCLI.dll (default: ../LocalControllerCLI's Release build)
#
# Ports 18129/22129 for the grid operator and 18127/22127 for the local
# controller, the ones the two start tests in CI use.
#
# No jq and no python: the Debian container of the nightly has neither, so the
# few values needed are cut out of the JSON with sed.

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
mkdir -p "$tmp/go" "$tmp/lc"

go_url="http://127.0.0.1:18129"
go_pid=""
lc_pid=""

stop() {
    [ -n "$lc_pid" ] && kill "$lc_pid" 2>/dev/null
    [ -n "$go_pid" ] && kill "$go_pid" 2>/dev/null
    [ -n "$lc_pid" ] && wait "$lc_pid" 2>/dev/null
    [ -n "$go_pid" ] && wait "$go_pid" 2>/dev/null
    return 0
}

fail() {
    echo "$1"
    stop
    echo
    echo "What the grid operator said, without the first-start password:"
    grep -v -i password "$tmp/go/console.log" 2>/dev/null | tail -60 || true
    echo
    echo "What the local controller said, without the first-start password:"
    grep -v -i password "$tmp/lc/console.log" 2>/dev/null | tail -60 || true
    exit 1
}

# A request to the grid operator's JSON API as root: the status code on the
# first line of the answer file, the body below it.
api() {
    local method="$1" path="$2" body="${3:-}"
    local code
    if [ -n "$body" ]; then
        code="$(curl -sS -m 15 -u "root:$password" -X "$method" -H 'Content-Type: application/json' -d "$body" \
                     -o "$tmp/answer.json" -w '%{http_code}' "$go_url$path" 2>/dev/null || true)"
    else
        code="$(curl -sS -m 15 -u "root:$password" -X "$method" \
                     -o "$tmp/answer.json" -w '%{http_code}' "$go_url$path" 2>/dev/null || true)"
    fi
    echo "${code:-000}"
}

# The string value of the first "Name": "..." in a JSON file.
value() {
    sed -n "s/.*\"$1\" *: *\"\\([^\"]*\\)\".*/\\1/p" "$2" | head -n 1
}

# The number value of the first "Name": 123 in a JSON file.
number() {
    sed -n "s/.*\"$1\" *: *\\([0-9][0-9]*\\).*/\\1/p" "$2" | head -n 1
}

#region The grid operator

cat > "$tmp/go/configuration.json" <<'EOF'
{ "gridOperator": { "@id": "DE*SWJ", "name": "Netze Beispielstadt" } }
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

up=000
for _ in $(seq 1 60); do
    up="$(curl -sS -m 5 -o /dev/null -w '%{http_code}' "$go_url/" 2>/dev/null || true)"
    up="${up:-000}"
    [ "$up" != 000 ] && break
    kill -0 "$go_pid" 2>/dev/null || break
    sleep 1
done
[ "$up" = 200 ] || fail "The grid operator did not start ($up)."

# The banner shows root's password once, in a box: "│  password  <it>".
password=""
for _ in $(seq 1 20); do
    password="$(grep -a 'password ' "$tmp/go/console.log" | head -n 1 | awk '{ print $NF }')"
    [ -n "$password" ] && break
    sleep 0.5
done
[ -n "$password" ] || fail "The grid operator showed no password for root."

code="$(api POST /api/v1/gridConnectionPoints '{
    "@id": "GCP-1", "name": { "de": "Netzanschluss GCP-1" },
    "voltageLevel": "LowVoltage", "connectionType": "AC_ThreePhases",
    "nominalVoltage": 400, "nominalFrequency": 50,
    "contractedImportPower": 110000, "contractedImportApparentPower": 120000,
    "marketLocationIds": [ "50123456789" ] }')"
[ "$code" = 201 ] || [ "$code" = 200 ] || fail "The grid connection point was not added ($code): $(head -c 500 "$tmp/answer.json")"

code="$(api POST /api/v1/openADR/localControllers '{ "venName": "lc-e2e", "chargingPoolId": "DE*GEF*P1", "gridConnectionPointId": "GCP-1" }')"
[ "$code" = 201 ] || fail "The local controller was not added ($code): $(head -c 500 "$tmp/answer.json")"

vtn_url="$(value vtnURL "$tmp/answer.json")"
secret="$(value clientSecret "$tmp/answer.json")"
[ -n "$vtn_url" ] && [ -n "$secret" ] || fail "Adding the local controller said no VTN URL or no secret."

code="$(api POST /api/v1/openADR/limits '{ "kind": "IMPORT_CAPACITY_LIMIT", "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }')"
[ "$code" = 201 ] || [ "$code" = 200 ] || fail "The limit was not sent ($code): $(head -c 500 "$tmp/answer.json")"

echo "grid operator     up, root signed in, GCP-1, lc-e2e and a limit of 44 kW added; VTN at $vtn_url"

#endregion

#region The local controller

cat > "$tmp/lc/configuration.json" <<EOF
{ "openADR": { "enabled": true, "vtnURL": "$vtn_url", "clientID": "lc-e2e", "pollEvery": 5 } }
EOF

# The secret in its own file beside the configuration, as "openADR import"
# writes it - never a switch of the program.
printf '{ "clientSecret": "%s" }\n' "$secret" > "$tmp/lc/openadr-credentials.json"
chmod 600 "$tmp/lc/openadr-credentials.json" 2>/dev/null || true

dotnet "$lc_dll" \
    --port      18127 \
    --ssh-port  22127 \
    --accounts  "$tmp/lc/accounts" \
    --config    "$tmp/lc/configuration.json" \
    --log-file  "$tmp/lc/logs" \
    < /dev/null > "$tmp/lc/console.log" 2>&1 &
lc_pid=$!

#endregion

#region The limit read, and the report on it

reports=0
seen=""
for _ in $(seq 1 90); do
    kill -0 "$lc_pid" 2>/dev/null || fail "The local controller is no longer running."
    code="$(api GET /api/v1/openADR/localControllers)"
    if [ "$code" = 200 ]; then
        reports="$(number reports "$tmp/answer.json")"
        seen="$(value lastSeen "$tmp/answer.json")"
        [ "${reports:-0}" -ge 1 ] && break
    fi
    sleep 1
done

limit_read=0
grep -a -r -q "The grid operator sent a limit" "$tmp/lc/console.log" "$tmp/lc/logs" 2>/dev/null && limit_read=1

echo "local controller  last seen by the VTN: ${seen:-never}; limit read: $([ "$limit_read" = 1 ] && echo yes || echo no)"
echo "grid operator     reports from lc-e2e: ${reports:-0}"

[ -n "$seen" ]              || fail "The local controller never signed in at the VTN."
[ "$limit_read" = 1 ]       || fail "The local controller did not say it read the limit."
[ "${reports:-0}" -ge 1 ]   || fail "The grid operator got no report from the local controller."

#endregion

stop
echo "End to end: the limit went from the grid operator to the local controller, and the report came back."
