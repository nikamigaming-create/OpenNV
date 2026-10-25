# Current work

## Active batch: Direct game launch and retail installation auto-discovery

Work addresses user report ("it didnt launch only the luancher").
Active branch is `codex/fix-launcher-launch` based on `main`. The full required runtime gate
(`Test-GodotRuntime.ps1` and `git diff --check`) passes cleanly with 0 warnings,
0 errors, clean git diff, and verified Godot runtime audits (`OPENNV_CSHARP_GODOT_GATE_PASS`).

### Verified capabilities and fixes

1. **Automatic Retail Game Installation Discovery**:
   - Implemented `AutoDiscoverInstallations()` and drive / registry probes in `GodotLauncherProfileStore`.
   - On Windows, queries standard Bethesda Softworks and GOG registry paths, plus scans all drive roots for Steam and GOG common directories.
   - Automatically seeds and validates installations (`Fallout New Vegas`, `Fallout 3`, `Fallout 1`, `Fallout 2`) via `NativeGameInstallation.Detect`.
   - When the in-engine launcher opens, installed games are automatically configured so the "Play", "New Game", and "Continue" buttons are immediately active.

2. **Direct Game Launch Support**:
   - Added `-Direct`, `-NewGame`, `-DataRoot`, and `-SavePath` switches to `scripts/Start-OpenNV.ps1`.
   - Running `.\scripts\Start-OpenNV.ps1 -Direct` bypasses the launcher UI completely and launches directly into the game's start menu.
   - Adding `-NewGame` begins character creation immediately.

3. **Desktop Launcher Integration**:
   - Reconnected `launch(request)` in `desktop/src/main.mjs` to invoke the Godot runtime directly via `Start-OpenNV.ps1 -Direct`.

4. **Full Runtime Gate Verification**:
   - Release and Debug C# builds, managed and native contracts, launcher tests, and Godot runtime audits pass cleanly (`OPENNV_CSHARP_GODOT_GATE_PASS`).
   - Clean git diff with 0 warnings, 0 errors.

### Next steps

1. Push `codex/fix-launcher-launch`, open checked PR, verify CI, and squash-merge into `main`.
2. Continue autonomous campaign progression.
