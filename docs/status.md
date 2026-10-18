# Product status

Updated 2026-10-17. Priority: **vanilla FNV -> vanilla FO3 -> mods -> TTW**.
The user rejected package g's physical FNV VR playtest. It is not a playable or
headset-ready handoff. See [implementation-plan.md](implementation-plan.md) for
the evidence-backed repair inventory and work order.

| Area | Current state |
| --- | --- |
| FNV world | Exploration, interiors and some dialogue render in the user's recording. Missing vegetation/models/materials, physics and LOD behavior remain visible in diagnostics. No world/parity completion. |
| Population | User rejects NPC behavior. Logs report missing travel, dialogue-location, patrol, AI condition and creature procedure owners. A few moving actors do not establish a functioning population. |
| Combat | User rejects combat. Deathclaw attack variants, embedded emitters and faction assistance fail in the session log. Earlier gecko/Viper/coyote checks remain bounded only. |
| VR rig | Visible severe rifle hand/forearm deformation around source recording 32:10-32:48. Physical VR acceptance failed. |
| Launch/render | Visible local launcher plus Vulkan Continue recovered world output after a white D3D12 session. Shader, view-count/texture-array and XR retirement errors still require reproduction and repair. |
| Saves | Earlier spatial/residency fixes passed selected checks; this session has an actual failed manual save from unsupported hit-reaction NIF version 27 continuation. This is the next implementation blocker. |
| Quest/scripts | Some wrist pages and dialogue render. AddReputation, conversation conditions, script control flow, activation/trigger lookup and actor-value/effect owners fail in the session log. Campaign progression is unaccepted. |
| Audio/UI | Wrist page changes are visible; complete interaction/input release remains unaccepted. Radio broadcast scheduling is unbound. Audio mix/sync is not certified by this review. |
| Traversal | Cross-cell NPC/creature route owners, authored floor destinations and the prior Cheyenne saloon portal remain open. |
| Fast travel | Earlier bounded flat/XR travel worked. Final population and saving after travel remain unaccepted. |
| Performance | User session is rejected. Capture cadence does not prove headset frame timing; settled recording-free measurement is required. |
| Builds | Package g passed earlier build/hash/component checks, then failed the user's playtest. The selected private 90-second development edit is not acceptance evidence. |
| Content | Official ESM selection logged; installed loose/resource winners still need provenance review. Native extensions disabled; pristine vanilla is not established by plugin selection alone. |
| FO3 | Earlier rendered New Game reached birth stage 22. No accepted standalone FO3 VR release. |
| Mods/TTW | Deferred until standalone games work. |

No campaign completion, complete population/combat/weapon support, retail pixel
parity or physical-headset acceptance is claimed. Original owned files, saves
and the user's recording remain private and untouched.
