# Product status

Updated 2026-10-17. Priority: **vanilla FNV -> vanilla FO3 -> mods -> TTW**.

| Area | Current state |
| --- | --- |
| FNV startup regression | Fresh New Game completes Doc's farewell and saves at stage200. Exit now passes ForceWeather; its next PlayMusic command still blocks the tutorial reset. |
| Builds | Full gate passed with performance fixes. Next run found uninitialized vitals in a test fixture; corrected with defeated-player coverage. Final full gate pending. |
| OpenXR | Exported Release loads Goodsprings. Controller wrist UI/ray selection, input release, reload/fire, head motion, snap turns and saloon traversal exercised. Physical headset pending. |
| Performance | Readback-disabled outdoor median fell from about 33 ms to about 14 ms after particle/record fixes. Settled measurement without background checks remains pending. |
| Gameplay | Sunny dialogue works. Weather and tutorial weapon queries implemented/source-audited. Script music is the next fresh-route blocker. Combat targets, loot, crafting, Primm and ED-E remain regression work. |
| Saves | Saloon autosave writes and cold-loads correctly. Fresh character manual save works. The old copied save retains earlier script faults and a later furniture capture blocker; original untouched. |
| Profiles | Explicit official-content selection and isolated profile store. Native extensions disabled. Installed loose assets retain normal precedence. |
| FO3 | Rendered New Game reaches birth quest stage22 with working character-choice/menu controls and zero telemetry-publication failures. No accepted FO3 OpenXR release. |
| Mods/TTW | Deferred until clean standalone games work. |
| Repository | Recent work preserved at 17a56554; one recovery candidate and PR #207. Main promotion and branch/worktree cleanup pending. |
| Headset | Quest 3 / Air Link. User test follows simulator and exported Release checks. |

No campaign completion, full weapon coverage, visual parity or physical-headset
acceptance is claimed. See current-work.md for the current owner and next action.
