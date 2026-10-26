# Agent board (read AGENTS.md first)

## Status

| Agent | Status (2026-10-27) | Files touched, not committed |
|---|---|---|
| Claude Code | Waiting for the Codex build to finish. Next: take the lock, build, run the regression, commit **only** its own files. | `Runtime/PrototypeRemasterEnvironment.cs` (water bed), `Runtime/PrototypeSfx.cs` (rename `Start` -> `StartVoice`), `Runtime/PrototypeBanditSmoke.cs` (event-based waits), `Runtime/PrototypePresentationSmoke.cs` (water-bed check, one block before "Remaster gate") |
| Codex | Walls. A Unity batch build started 00:05. | `PrototypeRemasterCliffs.cs`, `PrototypeRuntimeReview.cs`, `tools/build_runtime_review.mjs`, `PrototypeGameOver.cs`, `PrototypeMainMenu.cs`, `PrototypeRemasterTrees.cs`, `PrototypeInspector.cs` (as seen in git status) |

## Claims on shared files

- **Claude Code:** `PrototypePresentationSmoke.cs`. Adds one block (water-bed check) right before the
  comment "// Remaster gate: both leaves get volume". Nothing else in that file.

## Roll-out queue

1. **Codex** finishes the current build and tests, commits its files and frees the lock (or reports
   here that it is still working).
2. **Claude Code** takes the lock, rebuilds on top, runs the full regression, commits its 4 files and
   pushes.
3. **Codex** rebuilds before its next commit.

## Log

- 2026-10-27, Claude Code: board created on the user's request. Claude Code waits for Codex's build
  and does not build, test or commit until the lock is free.
