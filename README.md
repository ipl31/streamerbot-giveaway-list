# Streamer.bot Giveaway List

Post everyone present in your Twitch chat and save a **timestamped history file**
with one login name per line, ready to paste into a spin wheel. Includes lurkers
connected to chat. Names are sorted and deduplicated.

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
7. If the complete list fits in one chat message, it appears in chat. Each run
   also creates **giveaway-names-<UTC timestamp>-<unique id>.txt** on the Desktop
   of the account running Streamer.bot. Copy that file's contents into your wheel.

The package targets Streamer.bot **1.0.4 or newer** and uses export format 23.
Use **v0.1.1 or newer**: v0.1.0 incorrectly rejected version 1.0.4 due to an
optional metadata version restriction. Automated package and behavior checks are provided, but native
Streamer.bot import and live Twitch presence have not yet been smoke-tested.
Follow [the smoke test](docs/SMOKE-TEST.md) before using it for a real giveaway.

## Output and configuration

The imported action shows three groups:

1. **Get all present Twitch users** — collapsed to keep the application's internal
   collector details out of the main list. Expanding it still shows Streamer.bot's
   fixed **Add Random Users** label; the count requests all available present users.
2. **Save timestamped giveaway file** — writes history and prepares the message.
3. **Post users to chat (if the list fits)** — expand this to see a native If/Else:
   **True** runs a Twitch Send Message sub-action containing the names; **False**
   runs a separate Twitch Send Message containing only the file-check notice.

Chat output is now performed by those visible native Twitch sub-actions, rather
than a C# chat call hidden inside the file-save step. The condition reads
`%giveawayListFitsChat%` and compares it with `True`. The save step sets this flag
using the complete prepared message length, including separators.

```text
alice
bob
quiet_lurker
```

Every successful export creates a new file with a UTC timestamp and unique ID;
existing history and older fixed-name exports are never overwritten. The file
is saved before posting to chat. Check Streamer.bot's log for its count and path.

Chat contains only usernames, separated by spaces by default. To switch to
comma-delimited output, set the persisted **string** global
`giveawayListChatDelimiter` to `comma`. Set it to `space` to switch back.
There is no heading, command, bullet, or trailing delimiter in the user list:

```text
space: alice bob quiet_lurker
comma: alice,bob,quiet_lurker
```

The labels above illustrate the settings; they are not posted in chat. Changes
take effect on the next command. If the global is missing, the first run with
users creates it as `space`. Blank or invalid settings use spaces; invalid
settings also log a warning. The timestamped files always retain one name per line.

If the complete message is **500 characters or fewer**, it is sent once. If it exceeds
500 characters, only this message is posted:

> User list is too long for chat. Mods: please ask the streamer to check the text file.

The full list is still saved in the history file. No filename or filesystem path
is posted in chat, and long lists are not truncated or split across messages.
Messages use the connected bot account, falling back to the broadcaster account.
An empty list or failed file write prepares a short generic status without
usernames or paths; the true branch posts that status and no new file is created.
Native chat delivery failures do not remove saved history; check Streamer.bot's
log. **Do not use an older export for a new giveaway.**

Optional **persisted string global variables**, editable in Streamer.bot's global
variable viewer (or set with Core → Globals → Global Set):

| Variable | Default | Meaning |
| --- | --- | --- |
| `giveawayListOutputPath` | blank | Absolute base filename. Each export adds a UTC timestamp and unique ID before its extension. Blank uses Desktop/giveaway-names.txt as the base; parent folder must exist. |
| `giveawayListExcludedLogins` | blank | Comma-separated Twitch login names to exclude, ignoring case and surrounding spaces. |
| `giveawayListChatDelimiter` | `space` | `space` or `comma`, case-insensitive. Controls chat only; text files stay one name per line. A literal comma is also accepted. |

By default everyone is included, including your account and connected bots.
The list represents the last presence refresh, not a fresh Twitch lookup when
the command runs. It cannot identify anonymous viewers watching the video.
The collection sub-action requests up to **100,000** present users. If fewer are
available, Streamer.bot returns the available users; names are sorted afterward.
Edits to command permissions alone cannot grant ordinary viewers export access:
the C# role check independently requires a broadcaster or moderator.

When upgrading to v0.3.0, re-import and overwrite the existing action/command,
then recheck that the command is enabled. An existing custom output path remains
the base for new timestamped filenames; no configuration migration is required.

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
and upload build artifacts. To publish a release, update `VERSION`, commit
the change, and push a matching tag:

```sh
git tag v0.3.0
git push origin main v0.3.0
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
- [Twitch Send Chat Message limit](https://dev.twitch.tv/docs/api/reference/#send-chat-message)
- [Streamer.bot SendMessage](https://docs.streamer.bot/api/csharp/methods/twitch/chat/send-message)
