# Native smoke test

Status: **not yet performed**. Automated CI does not launch Streamer.bot or
authenticate to Twitch. Record the application version, release tag and results
here when this test is performed. Use a disposable/test Streamer.bot profile.

1. Import the release `.sb`. Confirm one command, one action, and one matching
   Command Triggered trigger. Inspect Add Random Users: Count 100000,
   Present Only checked, no group restriction. Confirm C# compiles.
2. Confirm command permissions allow Moderators and the imported command is
   disabled until enabled. Enable it.
3. Connect the broadcaster account and enable Twitch Present Viewers Live Update
   with a one-minute refresh. Leave a second logged-in account connected to chat
   without sending messages, and wait for it to appear in the presence list.
4. Run `!giveawaylist` as broadcaster. Check the log count and Desktop output.
   Confirm the silent account is included and each name occupies exactly one line.
5. Run it as moderator and as ordinary viewer. Moderator should export; ordinary
   viewer should not change the file. Confirm bot accounts are included by default.
6. Set `giveawayListExcludedLogins` to a known account and repeat; verify removal.
   Set `giveawayListOutputPath` to an absolute filename in an existing temporary
   directory and confirm it writes there. Reset both variables afterward.
7. Use a nonexistent output directory and confirm a warning without a partial file.
   If no eligible users exist, confirm the log warns that any old export is stale.
8. Paste the successful file into the wheel and compare the entry count.
9. Re-import the same package. Review overwrite options and confirm stable IDs do
   not create duplicate actions/commands. Recheck permissions and enabled state.

Evidence to record: Streamer.bot version, OS, tag, import result, compilation
result, silent-account inclusion, roles checked, output count, and date.
