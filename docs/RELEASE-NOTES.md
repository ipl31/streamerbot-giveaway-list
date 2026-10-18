Post giveaway users in Twitch chat and keep a timestamped export history.

- Lists that fit Twitch's 500-character message limit are posted once as
  comma-separated names. The limit includes the prefix and separators.
- Longer lists post only: "User list is too long for chat. Mods: please ask the
  streamer to check the text file." No filename or path is posted.
- Every successful run also saves the full list, one name per line, in a new
  `giveaway-names-<UTC timestamp>-<unique id>.txt` file on the Desktop. Files are
  never overwritten, including when commands run in rapid succession.
- Custom `giveawayListOutputPath` values remain supported as base filenames;
  timestamps and unique IDs are appended before the extension.
- The file is saved before chat output. A failed save posts a generic error;
  a chat failure retains the saved history. Moderator/broadcaster access remains.

Download the `.sb` asset and re-import it, overwriting the existing action/command.
Recheck that `!giveawaylist` is enabled. Keep Twitch Present Viewers **Live Update**
enabled and allow a presence refresh. Streamer.bot 1.0.4 or newer is supported.

Automated package and C# behavior checks run on Linux and Windows, including
500/501-character boundaries, file history, and generic fallback messages.
Native Streamer.bot import/live Twitch chat testing remains outstanding; see
docs/SMOKE-TEST.md. The `.sb.sha256` asset records the package checksum.
