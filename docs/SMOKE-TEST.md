# Native smoke test

Status: **not yet performed**. Automated CI does not launch Streamer.bot or
authenticate to Twitch. Record the application version, release tag and results
here when this test is performed. Use a disposable/test Streamer.bot profile.

1. Import the release `.sb`. Confirm one command, one action, and one matching
   Command Triggered trigger. Confirm the three top-level groups read Get all
   present Twitch users, Save timestamped giveaway file, and Post users to chat
   (if the list fits). The first group should be collapsed. Expand it to inspect
   the internal collector: Count 100000, Present Only checked, no group restriction.
   Confirm C# compiles. Expand the final group and confirm its native If/Else tests
   `%giveawayListFitsChat%` Equals `True`, with a Twitch Send Message in each branch.
   The true branch sends `%giveawayListChatMessage%`; the false branch sends the
   generic moderator file-check notice. Both branches and messages must be enabled.
2. Confirm command permissions allow Moderators and the imported command is
   disabled until enabled. Enable it.
3. Connect the broadcaster account and enable Twitch Present Viewers Live Update
   with a one-minute refresh. Leave a second logged-in account connected to chat
   without sending messages, and wait for it to appear in the presence list.
4. Run `!giveawaylist` as broadcaster. Check the log count and Desktop output.
   Confirm the silent account is included and each name occupies exactly one line.
   Confirm short lists appear as plain usernames separated by spaces, without
   the old `Giveaway users:` heading. Set persisted string global
   `giveawayListChatDelimiter` to `comma` and verify `alice,bob` style output.
   Set it back to `space` and confirm history files use one name per line in both modes.
   Run it twice and confirm two timestamped files with all prior history intact.
5. Run it as moderator and as ordinary viewer. Moderator should export; ordinary
   viewer should create no file and post nothing. Confirm bot accounts are included by default.
6. Set `giveawayListExcludedLogins` to a known account and repeat; verify removal.
   Set `giveawayListOutputPath` to an absolute filename in an existing temporary
   directory and confirm new filenames use that base plus timestamp/unique ID.
   Reset both variables afterward.
7. Use a nonexistent output directory and confirm a warning without a partial file.
   Confirm chat reports the failure without usernames, filename or path.
   If no eligible users exist, confirm a generic status and no new export.
8. Paste the successful file into the wheel and compare the entry count.
9. Re-import the same package. Review overwrite options and confirm stable IDs do
   not create duplicate actions/commands. Recheck permissions and enabled state.
10. With a list exceeding 500 characters including the selected separators,
    confirm only the moderator file-check notice appears in chat,
    without any filename/path. Confirm the complete list is in the new history file.
11. If a separate bot account is connected, confirm chat messages use that account.
    Without it, confirm broadcaster fallback. Confirm saved history survives any
    chat delivery failure; inspect Streamer.bot's native message-action logs.

Evidence to record: Streamer.bot version, OS, tag, import result, compilation
result, silent-account inclusion, roles checked, output count, and date.
