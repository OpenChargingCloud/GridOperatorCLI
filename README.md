# GridOperatorCLI

A grid operator - a distribution system operator (DSO) - of the OpenChargingCloud, with its web interface, its
OpenADR 3 VTN and a prompt: the program around [GridOperator](https://github.com/OpenChargingCloud/GridOperator),
which pins it beside the libraries it needs.

- the grid operator it is in **WWCP POI 3**, with keys of its own, and its **grid connection points**, published
  to a roaming network with change sets it signs;
- an **OpenADR 3 VTN** for the local controllers of the charging pools behind those points: they sign in with
  client credentials, read the limits sent to them and report back.

```bash
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
copy of `gridOperator/` where nobody else can read it. `--help` lists the switches.

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


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
