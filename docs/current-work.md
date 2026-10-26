# Current work

## Active batch: Fullscreen direct game launch

Work addresses user request ("launch new game for me full screnn").
Active branch is `codex/support-fullscreen-launch` based on `main`.

### Verified capabilities and fixes

1. **Fullscreen Launch Support**:
   - Added `-Fullscreen` parameter to [Start-OpenNV.ps1](file:///d:/code/OpenNV/scripts/Start-OpenNV.ps1).
   - Added `--fullscreen` option handling to engine arguments before `--` and in [RuntimeCoordinator.cs](file:///d:/code/OpenNV/runtime/src/RuntimeCoordinator.cs) via `DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen)`.
   - Forwarded `request.fullscreen` option in [desktop/src/main.mjs](file:///d:/code/OpenNV/desktop/src/main.mjs).
   - Launched interactive New Game directly in fullscreen on the user's display using detected Fallout: New Vegas retail files.

2. **Runtime Gate and Builds**:
   - Clean export and release builds pass.
   - Zero git diff check errors (`git diff --check`).

### Next steps

1. Conclude playtest review and merge `codex/support-fullscreen-launch` into `main`.
2. Continue autonomous campaign progression.
