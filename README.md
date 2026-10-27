# GridOperatorCLI

[![CI](https://github.com/OpenChargingCloud/GridOperatorCLI/actions/workflows/ci.yml/badge.svg)](https://github.com/OpenChargingCloud/GridOperatorCLI/actions/workflows/ci.yml)
[![Nightly](https://github.com/OpenChargingCloud/GridOperatorCLI/actions/workflows/nightly.yml/badge.svg)](https://github.com/OpenChargingCloud/GridOperatorCLI/actions/workflows/nightly.yml)

A grid operator - a distribution system operator (DSO) - of the OpenChargingCloud, with its web interface, its
OpenADR 3 VTN and a prompt: the program around [GridOperator](https://github.com/OpenChargingCloud/GridOperator),
which pins it beside the libraries it needs.

- the grid operator it is in **WWCP POI 3**, with keys of its own, and its **grid connection points**, published
  to a roaming network with change sets it signs;
- an **OpenADR 3 VTN** for the local controllers of the charging pools behind those points: they sign in with
  client credentials, read the limits sent to them and report back.

The libraries it is built from are submodules, so they have to come along:

```
git clone --recurse-submodules https://github.com/OpenChargingCloud/GridOperatorCLI.git
```

If you already cloned it without them:

```
git submodule update --init --recursive
```

**On Windows**, turn long paths on first:

```
git config --global core.longpaths true
```

The deepest file in the submodules is 143 characters below the clone root, and under the classic 260-character
limit git creates no file whose whole path is longer than 259, so the root itself may be at most 115 characters
long. `D:\src\GridOperatorCLI` is fine; a checkout nested deep below `C:\Users\<you>\AppData\Local\Temp\...` can
run out of room, and then the clone fails halfway through a submodule with `Filename too long` rather than at the
start. Per clone instead of globally: `git clone -c core.longpaths=true ...`.

Then build and start it:

```bash
dotnet build GridOperatorCLI.slnx
./run.sh
```

At the first start it makes the account `root`, with a password shown once, and the grid operator: say who it is
in `configuration.json` before that first start, e.g.

```json
{
  "gridOperator":   { "@id": "DE*SWJ", "name": "Netze Beispielstadt" },
  "roamingNetwork": { "url": "http://127.0.0.1:2356/poi/RNs/QA" }
}
```

Its keys are kept in `gridOperator/` beside the file, the local controllers and the limits in `openADR/`; keep a
copy of `gridOperator/` where nobody else can read it. `--help` lists the switches: every node's, read by
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node), among them HTTPS's `--https`, `--https-cert <file>`,
`--https-key <file>`, `--redirect-port <number>` and `--trusted-proxy <address>`.

**`--any` puts the sign-in on the network.** The local controllers need the VTN beyond the loopback, and with it
come the password, the API keys, the session cookie and the client secrets the local controllers sign in with -
in the clear, unless the grid operator speaks HTTPS itself or a proxy in front of it does and is named as trusted,
both in the `web` section of `configuration.json`. Behind nginx:

```json
"web": { "trustedProxies": [ "127.0.0.1", "::1" ] }
```

With certbot's files and no proxy:

```json
"web": { "tls": { "certificate": { "fullchain": "/etc/letsencrypt/live/dso.example.org/fullchain.pem",
                                   "privateKey": "/etc/letsencrypt/live/dso.example.org/privkey.pem" },
                  "redirectPort": 80 } }
```

Or `"certificate": "store"`, with the identity imported by `--import-certificate tlsServerIdentity=<file>`. Neither
said, the grid operator warns in its log, below its banner and above every page of its web interface to its
administrators. Where it speaks HTTPS itself, the VTN's `tokenURL` says `https://` too.

At the prompt, or over SSH on port 22358:

| | |
|---|---|
| `gridOperator [party]` | the grid operator, its public keys; with `party` what the operator of a roaming network adds it with |
| `gridConnectionPoints [<id> \| add … \| remove <id>]` | its grid connection points |
| `localControllers [add … \| secret <venName> \| remove <venName>]` | the local controllers that may sign in at the VTN |
| `limits [send … \| cancel <id>]` | the limits sent to them |
| `publish [now]` | what publishing to the roaming network would change; with `now`, signed and committed |

| | |
|---|---|
| `libs/GridOperator` | the grid operator: backend and web interface |
| `libs/WWCP_POI` | POI 3: the grid operator, its grid connection points, change sets |
| `libs/WWCP_OpenADR` | the data structures of OpenADR 3 |
| `libs/WWCP_Core` | what WWCP_OpenADR is built on |
| `libs/WWCP_Node`, `Hermod`, `Styx`, `Norn`, `Urdr` | what every node of the family shares |

Every push is built and tested on Windows and in a Debian 13 container by [`ci.yml`](.github/workflows/ci.yml): the
grid operator's tests, the node's, both web interfaces, and one start that asks the web interface, the JSON API, the
OpenADR 3 VTN and the SSH server whether they answer. [`nightly.yml`](.github/workflows/nightly.yml) runs every test
of every library at the pinned submodules, and builds and tests once more with the submodules moved to their tips.


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
