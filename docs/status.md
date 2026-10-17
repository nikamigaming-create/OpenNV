# Product status

Updated 2026-10-17. Priority: **vanilla FNV -> vanilla FO3 -> mods -> TTW**.

| Area | Current state |
| --- | --- |
| FNV startup regression | Fresh failure reproduced in incomplete mandatory Player construction. Coherent playable implementation restored; fresh final-candidate New Game still required. |
| Builds | Restored Debug and Release compile. Full runtime gate is in progress. |
| OpenXR | Restored Debug simulator loads Goodsprings with visible world, hands, wrist and laser pistol. Controller locomotion works. Final input changes and Release behavior still need replay. |
| Performance | Debug outdoor session is slow; no crisp-VR claim. Release measurement and repair are next. |
| Gameplay | Historical copied save continues. Dialogue, combat, loot, crafting, Primm and ED-E remain current-candidate regression work. |
| Saves | Original user save is preserved. Restored runtime accepts a copied historical save; final cold-save roundtrip still required. |
| Profiles | Explicit official-content selection and isolated profile store. Native extensions disabled. Installed loose assets retain normal precedence. |
| FO3 | Second recovery phase. No accepted FO3 OpenXR release. |
| Mods/TTW | Deferred until clean standalone games work. |
| Repository | Recent work preserved at 17a56554; one recovery candidate and PR #207. Main promotion and branch/worktree cleanup pending. |
| Headset | Quest 3 / Air Link. User test follows simulator and exported Release checks. |

No campaign completion, full weapon coverage, visual parity or physical-headset
acceptance is claimed. See current-work.md for the current owner and next action.
