# Product status

Updated October 17, 2026 after source and repository inspection.

Delivery order: **vanilla New Vegas -> vanilla Fallout 3 -> mods -> TTW**.
The immediate deliverable is recovered FNV VR for a local Quest 3 / Air Link test.

| Area | Current evidence | Acceptance |
| --- | --- | --- |
| Prior FNV gameplay | User reports multiple VR playthroughs through Primm and ED-E, including dialogue, quests, combat and loot. Historical source/package identities and recorded routes are available for reproduction. | Regression baseline to restore; not a fresh current-build pass. |
| Current FNV frame integration | Required Main/Player consumers contain unconditional unimplemented paths. Shared failure stops gameplay and XR actions. | Blocked in source; first fresh vanilla failure still needs reproduction. |
| Vanilla selection | Launcher supports explicit native-plugins none and separate profile roots. | Configuration capability; not proof of a playable game. |
| Existing VR/gameplay systems | Source still contains tracked rig, wrist UI, weapon, dialogue, companion and loot owners. | Their current end-to-end behavior must be rerun. |
| Saves and CELL traversal | Existing implementations and historical checkpoints are present. Current integration includes incomplete frame/travel owners. | Current ordinary Save/Quit/cold Continue unverified. |
| FO3 | Shared and standalone implementation exists. | Second recovery phase, after FNV. |
| Native mods and TTW | Existing compatibility implementation and incomplete consumers remain in the tree. | Later opt-in phases; not prerequisites for vanilla recovery. |
| Repository | 72 local branches, 24 registered worktrees, 18 dirty worktrees at inspection; one open PR (#207). | Consolidation and cleanup pending. |
| Current branch | codex/fo3-model-load at 17eddb43 with 169 changed/untracked paths. main equals origin/main at 147a3f9d. | Current source work is not yet a clean main baseline. |
| Physical VR | Target is Quest 3 over Air Link. | User-worn acceptance follows simulator and exported-build checks. |

No runtime build, live gameplay, simulator or headset test was performed during
this planning reset. Older component-pass narratives are not current acceptance.

The executable recovery order and acceptance route are in
[implementation-plan.md](implementation-plan.md). The next action is in
[current-work.md](current-work.md).
