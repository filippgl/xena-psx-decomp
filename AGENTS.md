# Rules for AI agents working in this repository (Codex, Claude Code)

Two agents can work in this tree at the same time. Follow `.claude/rules/small-fixes.md` and the
coordination protocol below. The live board is `docs/agent-board.md`: read it before starting work,
building, testing or committing, and update it whenever your status changes.

## 1. Own areas

An agent edits only the files of its own area. To touch a file in another area or a shared file,
first write a claim line on the board (agent, file, purpose). Then keep the change small.

Current split (user, 2026-10-27):

- **Codex: walls.** Remaster rock and cliff walls (`PrototypeRemasterCliffs`, the rock relief in
  `PrototypeRemasterGeometry`), the runtime review tooling (`PrototypeRuntimeReview`,
  `tools/build_runtime_review.mjs`), and the files Codex already has open (`PrototypeGameOver`,
  `PrototypeMainMenu`, `PrototypeRemasterTrees`, `PrototypeInspector`).
- **Claude Code: everything else.** Water (`PrototypeRemasterEnvironment`), audio (`PrototypeSfx*`,
  `PrototypeMusic`), the outer world (`PrototypeRemasterOuterWorld`), combat, chakram, grass,
  creatures, menus other than the two above, and the smoke tests `PrototypeBanditSmoke` and
  `PrototypeSettingsSmoke`.
- **Shared** (claim on the board first): `PrototypePresentationSmoke.cs`, `PrototypeMotor.cs`,
  `docs/ai-progress.md`, `Editor/XenaMovementPrototype.cs`.

## 2. One Unity build or test run at a time

The Unity project `unity/XenaLightingVerification` and the player in `build/unity-movement` can be
used by only one agent at a time. This covers `tools/run_unity_import.mjs`, every
`tools/check_*_player.mjs` and `tools/run_inspector.py`.

1. **Take the lock.** Before the run, create `build/agent-lock.txt` with one line:
   `<agent> <ISO time> <purpose>`.
2. **If the lock exists**, wait. Delete it only when its owner's process has really ended (no Unity
   `-batchmode` process and no player process running). Never delete a live lock.
3. **Release the lock.** Delete it when your build and tests finish, including when they fail.

A green test result counts only for the build you ran it on. When the other agent rebuilds, your
earlier green result no longer verifies the new build.

## 3. Commits

- Stage files by explicit path (`git add <your files>`). Never use `git add -A`, `git add .` or
  `git add` on the whole `Runtime/` folder: the tree holds the other agent's unfinished work.
- Commit only files you changed, after your own build and the full regression pass under the lock.
- Never commit another agent's files, and never revert or overwrite them. If a shared file contains
  both agents' changes, settle it on the board first.
- Write the commit and the tested build on the board. The other agent rebuilds before its next
  commit.

## 4. Roll-out order

The agent that finishes first takes the lock. It then builds, runs the regression, commits its
files and pushes. The other agent waits, rebuilds on top of that and runs the regression again with
both changes before committing.

## 5. How a change is built, tested and shipped (Claude Code's procedure, to follow exactly)

Run all commands from `decomp/` in Git Bash. Each step runs in the foreground; wait for it to exit. Do not start
a second copy of a step while one is still running.

1. **Lock.** `echo "codex $(date -Iseconds) build+tests" > build/agent-lock.txt` (lock rules: section 2).

2. **Build** the player from the Unity project (takes about 5 minutes):

       node tools/run_unity_import.mjs --editor ../Unity63/Editor/Unity.exe --project unity/XenaLightingVerification --movement > build/unity-import/<agent>-vNNN-build.txt 2>&1

   - Exit 0 is required.
   - Compile errors: `grep -i "error CS" build/unity-import/<agent>-vNNN-build.txt`.
   - "another Unity instance is running" means the lock was not respected. Stop and check the board.
   - Output: `build/unity-movement/XenaMovementPrototype.exe`, the game the user plays. Its code is
     `XenaMovementPrototype_Data/Managed/Xena.Assets.dll`. The `XenaUnity` project is NOT the tested
     build.
   - Generated data is not committed. It lives in `unity/*/Assets/Imported/...`, is installed by the
     `tools/install_*.py` / `tools/export_*.mjs` scripts and goes into both Unity projects.

3. **Regression.** Run the suites one after another, never in parallel. Each runs the built player
   with a smoke flag and writes `build/unity-import/<name>-player-verification.json`:

       for s in movement combat settings chakram special_moves advanced_moves presentation bandit; do
         node tools/check_${s}_player.mjs > build/unity-import/<agent>-vNNN-$s.txt 2>&1; echo "$s exit $?"; done
       node tools/check_prototype_reports.mjs > build/unity-import/<agent>-vNNN-aggregate.txt 2>&1; echo "aggregate exit $?"

   - Every suite and the aggregate must exit 0.
   - On a failure, read `"error"` in the suite's verification JSON. A suite that fails once and passes
     on a rerun is a flake: write it down, do not call it green.
   - The smokes are `Runtime/Prototype*Smoke.cs`, started by `--<name>-smoke <out>` in the player.
     Add a check there for every new behaviour. Never weaken an existing check to make it pass.

4. **Visual check** for visible changes:
   - `python tools/run_inspector.py` writes `build/inspect/inspect-sheet.png` (see-through holes)
     and `build/inspect/walls-sheet-*.jpg` (every wall: Original | Remaster, left / front / grazing).
     It deletes the per-view images itself.
   - The presentation smoke also writes `build/unity-import/presentation-*.png`.
   - Look at the images before reporting. Delete temporary captures and dumps afterwards: the user
     asked us not to fill the disk.

5. **Progress note.** Add a short entry to `docs/ai-progress.md`: what changed, why, the build
   (`<agent>-vNNN`), the test results, and what is still open.

6. **Commit and push**, only your own files, staged by explicit path:

       git add <your files> docs/ai-progress.md && git commit -m "<what and why>" && git push origin HEAD

7. **Unlock.** `rm build/agent-lock.txt`. Then write the commit hash on the board.

8. **Report to the user:**
   - what was wrong and why;
   - what changed;
   - the tested build and its test results, including flakes;
   - what is still open.

   Prominent visual changes (walls, gate, props) are shown to the user as screenshots and count as
   done only after the user approves them.
