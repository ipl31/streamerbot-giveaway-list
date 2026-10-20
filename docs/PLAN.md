# Implementation plan requested from Astra

Prepared with the `gpt-6-astra` planning agent on 2026-10-17; implemented by the
parent coding agent. Scope: a private GitHub repository whose build pipeline
produces a versioned Streamer.bot extension release artifact.

1. Preserve the gist's one-name-per-line Twitch presence export. Add
   broadcaster/moderator authorization, optional exclusions/output configuration,
   and file-error handling.
2. Verify Streamer.bot's import envelope and serialized fields against actual
   official exports/application metadata. Keep a readable template and stable IDs.
3. Build `.sb` and SHA-256 assets with Python's standard library, embedding the
   readable C# source. Use deterministic compression.
4. Validate package structure, linked IDs, embedded source, reproducibility and
   release versions. Compile/run the source with a controlled API stub to test
   filtering, duplicates, empty results, authorization and write failures.
5. Run CI on pushes/PRs and publish tag-triggered GitHub releases with the assets
   only after checks pass. Preserve the gist's visibility with a private repo.
6. Document import, enabling the command, Live Update, output and troubleshooting.
   Explicitly distinguish automated checks from a native Streamer.bot smoke test.

The initial Astra plan identified the random-user sub-action schema as needing
stronger verification before packaging. See FORMAT.md for the resulting evidence.

## Direct Get Chatters plan (v0.4.0)

Requested from Astra with correctness and code quality as the priority.

1. Replace the cache-backed native collector with a direct-fetch C# phase. Share
   typed pagination/authentication logic with the save phase through build-time
   source variants, identified by stable sub-action IDs. Reject ambiguous bindings.
2. Authorize the caller before touching credentials/network/files. Read the native
   broadcaster OAuth token/client ID; validate scope, expiry and broadcaster/client
   identity. Use the broadcaster ID for both Get Chatters identity parameters.
3. Follow all cursor pages, deduplicate IDs then normalized logins, sort, and only
   publish a complete string[]/bool handoff. Clear it on start/failure and consume
   it on save. Never substitute cached, prior, or partially fetched users.
4. Bound request time, whole-run time, transient retries, response size and page
   count. Honor retry delays; fail clearly instead of truncating. Keep credentials,
   raw response bodies and unrestricted exception details out of logs/arguments.
5. Preserve existing history-file, permissions, exclusions and native chat behavior.
   Use bundled JSON and framework HTTP adapters without machine-specific references.
6. Unit-test multi-page rosters over 1,000 entries, silent users, changing totals,
   duplicates/renames, encoded/repeated cursors, empty/malformed data, token identity/
   scope failures, 401/403/429/5xx, retries/deadlines, stale/partial handoff rejection,
   unauthorized credential avoidance, and secret sanitization. Retain file/chat
   regression tests and strict import structure checks.
7. Compile both actual embedded variants against the official 1.0.4 API and minimal
   Framework references; test production HTTP/JSON adapters on Windows Framework.
   Review found native namespace wrapping invalidated a global helper import;
   relative qualified types fix it, and wrapped compilation now guards regression.
8. Publish only after Linux/Windows CI passes. GUI import/live Twitch validation
   remains explicitly separate from automated tests.
