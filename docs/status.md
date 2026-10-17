# Product status

Updated 2026-10-17. Priority: **vanilla FNV -> vanilla FO3 -> mods -> TTW**.

| Area | Current state |
| --- | --- |
| FNV startup regression | Fresh failure reproduced in incomplete mandatory Player construction. Coherent playable implementation restored; fresh final-candidate New Game still required. |
| Builds | Full gate passed with performance fixes. Dialogue/autosave/audio fixes passed focused checks; next full gate pending. |
| OpenXR | Exported Release loads Goodsprings. Controller wrist UI/ray selection, input release, reload/fire, head motion, snap turns and saloon traversal exercised. Physical headset pending. |
| Performance | Readback-disabled outdoor median fell from about 33 ms to about 14 ms after particle/record fixes. Settled measurement without background checks remains pending. |
| Gameplay | Sunny exposed a missing death-state condition; general binding and owned tests added, actual replay next. Combat targets, loot, crafting, Primm and ED-E remain regression work. |
| Saves | Original save preserved. Door autosave initialization race now uses the existing deferred owner; a written save and cold roundtrip still required. |
| Profiles | Explicit official-content selection and isolated profile store. Native extensions disabled. Installed loose assets retain normal precedence. |
| FO3 | Rendered New Game reaches birth quest stage20. Audio readiness no longer throws away telemetry; next replay pending. No accepted FO3 OpenXR release. |
| Mods/TTW | Deferred until clean standalone games work. |
| Repository | Recent work preserved at 17a56554; one recovery candidate and PR #207. Main promotion and branch/worktree cleanup pending. |
| Headset | Quest 3 / Air Link. User test follows simulator and exported Release checks. |

No campaign completion, full weapon coverage, visual parity or physical-headset
acceptance is claimed. See current-work.md for the current owner and next action.
