# Product status

Updated 2026-10-18. Priority: **vanilla FNV -> vanilla FO3 -> mods -> TTW**.
The user rejected package g's physical FNV VR playtest. It is not a playable or
headset-ready handoff. See [implementation-plan.md](implementation-plan.md) for
the evidence-backed repair inventory and work order.

| Area | Current state |
| --- | --- |
| FNV world | Exploration, interiors and some dialogue render in the user's recording. Missing vegetation/models/materials, physics and LOD behavior remain visible in diagnostics. No world/parity completion. |
| Population | User rejects NPC behavior. Logs report missing travel, dialogue-location, patrol, AI condition and creature procedure owners. A few moving actors do not establish a functioning population. |
| Combat | User rejects combat. Unique compatible attack-group binding now passes source deathclaw/coyote hit, range-miss and cold-clock fixtures. Ordinary deathclaw terrain encounter, embedded emitters and faction assistance remain open. |
| VR rig | Visible severe rifle hand/forearm deformation around source recording 32:10-32:48. Physical VR acceptance failed. |
| Launch/render | Visible local launcher plus Vulkan Continue recovered world output after a white D3D12 session. Shader, view-count/texture-array and XR retirement errors still require reproduction and repair. |
| Saves | Stream-27/empty-note layout and finite player/effect audio repairs pass. Ordinary F5 during a qualifying coyote limb reaction completes; separate-process Continue retains its exact saved phase, injury/health, completes the reaction and resumes combat. Older unsettled off-cell audio continuation and explicit save stalls remain open. Physical VR persistence acceptance is pending. |
| Quest/scripts | Some wrist pages and dialogue render. AddReputation, conversation conditions, script control flow, activation/trigger lookup and actor-value/effect owners fail in the session log. Campaign progression is unaccepted. |
| Audio/UI | Wrist page changes are visible; complete interaction/input release remains unaccepted. Radio broadcast scheduling is unbound. Audio mix/sync is not certified by this review. |
| Traversal | Cross-cell NPC/creature route owners, authored floor destinations and the prior Cheyenne saloon portal remain open. |
| Fast travel | Earlier bounded flat/XR travel worked. Final population and saving after travel remain unaccepted. |
| Performance | Fresh optimized flat recording-free baseline retains roughly 26-29 ms settled rolling p95. A subsequent approach records a 203 ms streaming upload and 83 ms commit. Particle work, wrapper finalization and loading publication remain active repairs; no headset timing acceptance. |
| Builds | Package g passed earlier build/hash/component checks, then failed the user's playtest. The selected private 90-second development edit is not acceptance evidence. |
| Content | Official ESM selection logged; installed loose/resource winners still need provenance review. Native extensions disabled; pristine vanilla is not established by plugin selection alone. |
| FO3 | Earlier rendered New Game reached birth stage 22. No accepted standalone FO3 VR release. |
| Mods/TTW | Deferred until standalone games work. |

No campaign completion, complete population/combat/weapon support, retail pixel
parity or physical-headset acceptance is claimed. Original owned files, saves
and the user's recording remain private and untouched.
