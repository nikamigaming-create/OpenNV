# Product status

Updated 2026-10-20. User priority: **fully playable flat Fallout: New Vegas**.
FO1 remains an incomplete classic preview with selected movement, inventory,
equipment, door and save flows. Complete campaign execution and 1:1 3D art are
unverified. See [current-work.md](current-work.md) for the active owner.

The following FNV inventory is active repair work. FO3 and classic work are preserved.
The user rejected package g's physical FNV VR playtest. It is not a playable or
headset-ready handoff. See [implementation-plan.md](implementation-plan.md) for
the evidence-backed repair inventory and work order.

| Area | Current state |
| --- | --- |
| Flat UI lifetime | Loading tips now retain borrowed shared font atlases. Normal cold Continue, an exterior/interior/exterior door cycle and F5 pass without the former disposed-image HUD/message failure. HP/AP, reticle and activation text draw. Full runtime checks pass. |
| Flat interaction recovery | An aimed source failure no longer permanently disables HUD updates or later valid prompts. The native rendered check retains HP/AP, updates damage and restores exact valid-target pixels; full runtime checks pass. Linked-door lock inheritance itself remains unimplemented, and the failed target advertises no action. |
| Flat dialogue camera | Shared speaker focus and furniture pitch reset pass owned camera checks, a fresh-process questionnaire visual check and the full runtime gate. Normal play completes the questionnaire, tag skills and traits, exits the couch and saves at stage 110. Dialogue zoom/timing and matched retail framing remain open. |
| Flat object handling | The native-player Grab handler now uses source dynamic bodies, weight limits and bounded force. The owned skull fixture passes motion, collision/obstacle, release, refusal and source-event checks. Pending grab/release marks survive schema-v53 saves; v52 saves remain admitted. Ordinary room placement and physics save/cold continuation remain open. |
| FNV world | Exploration, interiors and some dialogue render in the user's recording. Missing vegetation/models/materials, physics and LOD behavior remain visible in diagnostics. No world/parity completion. |
| Population | User rejects NPC behavior. Logs report missing travel, dialogue-location, patrol, AI condition and creature procedure owners. A few moving actors do not establish a functioning population. |
| Automatic greeting / actor lifetime | Source editor-position dialogue waits retain native approach state. Ordinary saloon entry, F5 while waiting, cold Continue, walking into Sunny's voiced greeting, Goodbye and another F5 pass. Source corpses skip living AI; detached diagnostics cannot change another owner's corpse capture admission. Full runtime gate and synthetic contracts pass. Sandbox work and Victor's creature patrol remain unimplemented. |
| Combat | User rejects combat. Unique compatible attack-group binding now passes source deathclaw/coyote hit, range-miss and cold-clock fixtures. Ordinary deathclaw terrain encounter, embedded emitters and faction assistance remain open. |
| VR rig | Visible severe rifle hand/forearm deformation around source recording 32:10-32:48. Physical VR acceptance failed. |
| Launch/render | The D3D12 simulator candidate uses a render boundary for launcher entry and preserves stereo through process cleanup. Direct/launcher close checks report zero view-count/type errors and unchanged checkpoints. A separately identified experimental source template also clears the profile RID leak in synthetic/shared-path and owned direct/launcher checks; the official product engine still leaks it. Native API-hash verification and source-template promotion remain pending. Physical D3D12 white output, shader failures, continuous stereo and headset lifecycle acceptance remain open. |
| Saves | Stream-27/empty-note layout and finite player/effect audio repairs pass. Ordinary F5 during a qualifying coyote limb reaction completes; separate-process Continue retains its exact saved phase, injury/health, completes the reaction and resumes combat. Older unsettled off-cell audio continuation and explicit save stalls remain open. Physical VR persistence acceptance is pending. |
| Quest/scripts | Some wrist pages and dialogue render. AddReputation, conversation conditions, script control flow, activation/trigger lookup and actor-value/effect owners fail in the session log. Campaign progression is unaccepted. |
| Audio/UI | Wrist page changes are visible; complete interaction/input release remains unaccepted. Radio broadcast scheduling is unbound. Audio mix/sync is not certified by this review. |
| Traversal | Cross-cell NPC/creature route owners, authored floor destinations and the prior Cheyenne saloon portal remain open. |
| Fast travel | Earlier bounded flat/XR travel worked. Final population and saving after travel remain unaccepted. |
| Performance | Source-owned immutable DDS reuse reduces the selected player construction from 208 uploads to 51 with identical 101 audited actor-part bindings and verified retirement. An ordered, contended loading pair is 30.26 versus 21.17 seconds; the latter still has a 1,063 ms interval. Streaming, combat spikes, save capture/write and headset timing remain open. |
| Builds | Package g passed earlier build/hash/component checks, then failed the user's playtest. The selected private 90-second development edit is not acceptance evidence. |
| Content | Official ESM selection logged; installed loose/resource winners still need provenance review. Native extensions disabled; pristine vanilla is not established by plugin selection alone. |
| FO3 | Earlier rendered New Game reached birth stage 22. No accepted standalone FO3 VR release. |
| Mods/TTW | Deferred until standalone games work. |

No campaign completion, complete population/combat/weapon support, retail pixel
parity or physical-headset acceptance is claimed. Original owned files, saves
and the user's recording remain private and untouched.
