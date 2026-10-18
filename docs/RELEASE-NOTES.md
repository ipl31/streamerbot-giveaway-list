Plain usernames in chat, with a global setting for space or comma delimiters.

- Default: `alice bob quiet_lurker`.
- Set persisted **string** global `giveawayListChatDelimiter` to `comma` for
  `alice,bob,quiet_lurker`; set it to `space` to switch back.
- No `Giveaway users:` prefix, commands, bullets, or trailing separators.
- Changes apply on the next command. A missing global is initialized to `space`
  on the first run with users. Blank/invalid values fall back to spaces.
- Timestamped text files remain one username per line in both modes, preserving
  history. The chat delimiter does not change the file format.
- The complete list, including delimiters, must fit Twitch's 500-character limit.
  Oversized lists still post only the moderator file-check notice, with no filename.

Download the `.sb` and re-import over the existing action/command. Ensure the
command is enabled. The visible native Twitch Send Message branches remain.
Streamer.bot 1.0.4 or newer is supported; Live Update must be enabled for lurkers.

Automated checks cover both delimiters, default/global initialization, invalid
settings, plain output, unchanged file format, and 500/501-character routing.
Native Streamer.bot import/live Twitch chat testing remains outstanding.
