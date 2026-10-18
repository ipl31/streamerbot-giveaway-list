# Streamer.bot Giveaway List

Export everyone present in your Twitch chat as **one login name per line**, ready
to paste into a spin wheel. Includes lurkers connected to chat. Names are sorted
and deduplicated; this extension does not spin the wheel or choose a winner.

## Install

1. Download `streamerbot-giveaway-list-<version>.sb` from
   [Releases](https://github.com/ipl31/streamerbot-giveaway-list/releases).
   Use the `.sb` asset, not GitHub's source ZIP.
2. In Streamer.bot, click **Import**, drag the `.sb` into the **Import String**
   box, review the action and command, then click **Import**.
3. In **Commands**, enable **Giveaway List — !giveawaylist**. Imported commands
   are disabled by default. The package permits Moderators; the broadcaster is
   exempt from command restrictions. The code also checks the caller's role.
4. Connect your broadcaster account under **Platforms → Twitch → Accounts**.
5. Under **Platforms → Twitch → Settings → Present Viewers**, enable
   **Live Update**, choose **1 minute**, and allow a refresh before using it.
   Without Live Update, recent-activity tracking may omit lurkers.
6. As the broadcaster or a moderator, type `!giveawaylist` in Twitch chat.
7. Open **giveaway-names.txt** on the Desktop of the account running Streamer.bot.
   Copy its contents into your wheel.

The package targets Streamer.bot **1.0.4 or newer** and uses export format 23.
Use **v0.1.1 or newer**: v0.1.0 incorrectly rejected version 1.0.4 due to an
optional metadata version restriction. Automated package and behavior checks are provided, but native
Streamer.bot import and live Twitch presence have not yet been smoke-tested.
Follow [the smoke test](docs/SMOKE-TEST.md) before using it for a real giveaway.

## Output and configuration

```text
alice
bob
quiet_lurker
```

Each successful export replaces the file. An empty result or failed write leaves
the old file untouched and logs a warning: **do not use an older export for a new
giveaway**. Check Streamer.bot's log for the successful export count and path.
The extension sends no chat messages and uploads no names.

Optional **persisted string global variables**, editable in Streamer.bot's global
variable viewer (or set with Core → Globals → Global Set):

| Variable | Default | Meaning |
| --- | --- | --- |
| `giveawayListOutputPath` | blank | Absolute output filename; blank uses Desktop/giveaway-names.txt. Parent folder must exist. |
| `giveawayListExcludedLogins` | blank | Comma-separated Twitch login names to exclude, ignoring case and surrounding spaces. |

By default everyone is included, including your account and connected bots.
The list represents the last presence refresh, not a fresh Twitch lookup when
the command runs. It cannot identify anonymous viewers watching the video.
The collection sub-action requests up to **100,000** present users. If fewer are
available, Streamer.bot returns the available users; names are sorted afterward.
Edits to command permissions alone cannot grant ordinary viewers export access:
the C# role check independently requires a broadcaster or moderator.

## Build and release

Requirements: Python 3.12 and .NET SDK 8. No Python packages or test frameworks
are required. The tests compile the actual extension source with C# 7.3 syntax
against a small Streamer.bot API stub; they do not run Streamer.bot itself.

```sh
python -m unittest discover -s tests -v
dotnet run --project tests/BehaviorTests --configuration Release
python scripts/build.py
```

Outputs: `dist/streamerbot-giveaway-list-<VERSION>.sb` and `.sb.sha256`.
The readable template is in `extension/template.json`; the builder embeds
`src/GiveawayList.cs` and creates the import envelope. Stable IDs let future
imports identify the same command/action; review overwrite choices on upgrades.

Pushes and pull requests run package and behavior checks on Linux and Windows
and upload build artifacts. To publish a preview release, update `VERSION`, commit
the change, and push a matching tag:

```sh
git tag v0.1.1
git push origin main v0.1.1
```

The release job runs only after both operating systems pass, validates the tag,
and attaches the `.sb` file and checksum to a GitHub release. Native smoke testing
remains outstanding and is documented in the release notes. See
[Astra's implementation plan](docs/PLAN.md) and [format evidence](docs/FORMAT.md).

## References

- [Streamer.bot import/export](https://docs.streamer.bot/guide/core/import-export)
- [Present Viewers and Live Update](https://docs.streamer.bot/api/triggers/twitch/general/present-viewers)
- [Add Random Users](https://docs.streamer.bot/api/sub-actions/twitch/user/add-random-users)
- [Command permissions](https://docs.streamer.bot/guide/core/commands)
