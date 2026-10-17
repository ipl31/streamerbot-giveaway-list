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
