# OpenNV continuation

Updated September 30, 2026. Read current-work.md, architecture.md, status.md and
implementation-plan.md first. Work without subagents on fresh codex/ branches
from synchronized main, publishing each completed block through a checked PR.

The reboot's source-object-animation WIP is integrated. The selected checks cover
managed object group modes, script binding, conservative saved failure recovery,
v22 animation state, warm/cold/replacement ownership and the six plant families.
The exact ordinary flat result, active diagnostics, next owner and candidate
boundary are maintained in [current work](current-work.md).

Resume the earliest flat script/routine failure through shared runtime owners.
Do not discard the complete flat/VR, campaign, mod or recovery requirements.
Keep recording off, use copies of genuine saves and retain the protected
original checkpoints and requested reels identified in current-work.md.

Owned data: `D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data`.
Before pushing runtime or claim changes, run the selected owned-data audit and:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

The animation audit is:

```powershell
& 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path runtime res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn -- --object-animation 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data' 'D:\code\OpenNV\local\playtest-20260927-world\save.json'
```

A component pass is not ordinary play, whole-game completion, retail parity or
physical-headset acceptance. The experimental package must be refreshed from
clean merged runtime before calling it the current candidate.
