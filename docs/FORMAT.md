# Import format evidence

The builder generates an import file; it does not claim to export from a running
Streamer.bot installation. `exportedFrom: 1.0.4` identifies the verified schema
baseline. Minimum supported version is set to 1.0.4.

## Minimum-version boundary fix (v0.1.1)

The 1.0.4 native importer parses two separate minimum-version fields. Inspection
of the official executable confirms that the root `minimumVersion` is checked
with `ComparePrecedence(current, minimum) >= 0`, while the optional
`meta.minimumVersion` is checked with `ComparePrecedence(current, optional) > 0`
when supplied. The optional field defaults to no restriction when null.

v0.1.0 set both fields to `1.0.4`, causing the optional gate to reject equal
versions. v0.1.1 retains root `minimumVersion: "1.0.4"` and sets
`meta.minimumVersion: null`, as in the official example. The regression test checks
that the generated package accepts the equal-version boundary under those native
predicates and continues to reject earlier versions. This is based on inspection
of the actual predicates, not a completed GUI import test.

## Envelope and action structure

Examined the official chatbot example at documentation commit
`56253da4b3aebf70a1d134356368f1b13fcecd0e`:

https://github.com/Streamerbot/docs/blob/56253da4b3aebf70a1d134356368f1b13fcecd0e/streamerbot/4.examples/.imports/chatbot-commands.sb

Its SHA-256 is
`bc2618615c6d7d8cb5bb1af1803229bfd447a10f35611a0ef6c767bd7eac16d0`.
The file decodes as Base64 → four ASCII bytes `SBAE` → gzip → UTF-8 JSON.
The JSON has export-format `version: 23`, `exportedFrom: 1.0.4`, and a `data`
object containing actions, commands, queues, timers and websocket definitions.
Command Triggered uses type 401 with a `commandId`; Execute C# Code uses type
99999 with Base64 UTF-8 source in `byteCode`. It is not compiled IL.

`scripts/verify_upstream.py` independently downloads and checksum-verifies this
official fixture in CI, then checks that our decoder reads the expected native
structure. It does not execute upstream source. This supplements local roundtrip
tests, which alone would not demonstrate compatibility.

The official [2024 starter pack](https://r2.streamer.bot/imports/2024-starter-pack.sb)
contains actual commands with `permittedGroups: ["Moderators"]`, `grantType: 0`,
and empty `permittedUsers`. The template retains those permission values while
using the newer scalar `sources: 1` representation from the 1.0.4 example.

## Visible groups and native chat branches (v0.2.1)

The official 1.0.4 chatbot fixture's `!uptime` action provides a native group
example: `type: 99900`, `name`, `color`, `random: false` and nested `subActions`.
Each child carries its group's `parentId` and an index within that group. Its
`collapsedGroups` array contains the group UUID. The extension uses that same
structure to show a collapsed **Get all present Twitch users** collection step.

The fixture also contains native If/Else trees (`type: 120`) with true/false
folders (`99901`/`99902`). Native Twitch Send Message uses `type: 10`, `text`,
`useBot` and `fallback`. The post group uses these native objects to route
`%giveawayListFitsChat%` Equals `True` (operation 0, auto-typed) to the prepared
list, and otherwise to the generic moderator notice. The official starter pack
provides Equals/operation-0 examples; the 1.0.4 application logic model was also
inspected to confirm input/value parsing and operation 0 equality.

Only the native message leaves send to Twitch. C# prepares the text and length
flag and writes the file. The builder recursively embeds source inside groups;
tests verify parent IDs, indexes, collapsed state, enabled branches and messages,
and that the save source contains no chat-send call.

## Add Random Users

The required fields were checked against the official Streamer.bot **1.0.4**
application, downloaded from:

https://streamer.bot/api/releases/streamer.bot/1.0.4/download

ZIP SHA-256:
`de660b8a86eaeeccab0c7f22ebdd7102b6671ff59fee2839294c843454e1ce36`.
Inspection of the model carrying the `TwitchAddRandomUsers` attribute confirmed
these JSON properties and types:

| Property | Type | Packaged value |
| --- | --- | --- |
| `type` | numeric sub-action identifier | `22` |
| `present` | Boolean | `true` |
| `count` | String | `"100000"` |
| `includeFrom` | Boolean | `false` |
| `groups` | List of strings | `[]` |

The native constructor defaults `present` to true and `includeFrom` to false.
An empty `groups` collection passes no group filter. The independent
[streamerz schema](https://github.com/PedroZamecki/streamerz/blob/main/src/types.rs)
also identifies type 22 and these field names. **`presentOnly` is not a serialized
property**; using the UI label as a guessed JSON key would be wrong.

The full proprietary application/decompiled source is not distributed here.
These schema checks establish field compatibility, not successful GUI import or
live Twitch behavior. See SMOKE-TEST.md for the remaining native checks.
