# Current work

Updated 2026-10-17. The user's flat playtest rejected package f: missing quest
text, passive enemies, missing creatures and shadow pop-in. It is not an accepted
playtest or headset candidate. Root alone owns repairs, builds, live input and
Git. Priority remains FNV, standalone FO3, mods, then TTW.

## Confirmed failures and repairs under validation

- The quest page inserted a checkmark absent from the owned bitmap font and
  aborted drawing. Quest state survived. Completed objectives now use a supported
  marker; the copied user save visibly renders the completed bottle objectives
  and active Follow Sunny objective at tutorial stage 30.
- Exterior saves retained the streaming anchor after the player crossed cells.
  The user's latest save reproduced a rejected Continue. Capture now resolves the
  player's source spatial cell; loading resolves older positions in their saved
  world. The copied save loads without changing the original.
- Actor residency used old placement rather than authoritative package/combat
  motion. It now uses current source-space poses, including cold restoration,
  while honoring pending MoveTo before its native node catches up. Synthetic
  exterior/cold/scale/MoveTo checks pass. Sunny is present in rendered continuation,
  walks to the well and completes her conversation: tutorial stages 25 -> 30 -> 35.
- A queued autosave could wait indefinitely for a quiet boundary among repeating
  ambient sounds and reject every player save as concurrent. A fully admitted
  player snapshot now fulfills that pending automatic snapshot after successful
  writing. Actual near-Sunny F5 moved from queued-auto=true to a completed v52
  slot and queued-auto=false after genuine finite audio completion. The slot
  resolves spatial CELL 0e1ab6 and retains tutorial stage 35. The packaged export
  cold-loads it with Sunny at the saved well position, renders the quest page
  and commits another complete F5 slot.
- Cazador skin partition 214 was rejected. Source body regions now cover index 14
  and corresponding caps/sections. Template flags without an actual TPLT link now
  retain local actor fields. Owned construction, independent instances and cold
  animation continuation pass.
- Model-free melee weapons incorrectly required a projectile muzzle. Only ranged
  embedded weapons require that socket. Combat now owns its script-visible
  procedure/package before hit-reaction conditions, independently of unsupported
  ordinary packages. Owned armed and melee Viper component checks detect, pursue,
  retaliate and apply actual damage.
- AIDT aggro-radius warnings were absent. Warning target, count, clocks and idle
  identity now persist, with explicit v51 admission into v52. Owned coyotes warn
  in the outer radius, enter combat on source timers and cause damage. Friendly
  exclusions and confidence remain authoritative. Warning dialogue/special idles
  remain unbound and visible in telemetry.
- Pursuit lost source arrival-region information and could discard unfinished
  queries when a target moved. Region ownership now reaches native capsule
  planning. Terminal portal seeds inside the target region retain attack range.
  Sloped source-floor admission now projects vertically rather than shifting
  sideways. Chase and hit resolution share 3D range, preventing endless missed
  swings uphill. Blocked-target, prefix, floor and sloped fight checks accompany the repair.
  The copied user's actual-terrain Vulkan replay detects the player, climbs the
  slope and lands source melee hits, reducing health from 120 to zero.
- Exterior sunlight used Godot's default 100m shadow cutoff and unblended
  cascades. Shadow distance now covers the resident grid with cascade blending
  and observable settings. Owned native landscape checks pass. Building/ground
  shadows are visible during the rendered Sunny walk with 331.048m range and
  blended cascades. LOD shadow/filtering or retail pixel parity is not claimed.

## Verification and next owner

The user's owned quest/script snapshot retains all 447 instance clocks and
failures across cold restoration. Its audit now compares continuation collections
structurally instead of treating newly allocated equal arrays as changed state.
Selected owned creature, coyote warning, armed/melee NPC and landscape checks pass
their functional checks. The two NPC fixtures retain audio playback shutdown
warnings; those are not a leak-free-runtime claim. The complete runtime gate is
passed after the residency/save repair, including Release/Debug, format/analyzers,
contracts, launcher and native Godot probes. Diff checks pass. Independent
Release cold/save/UI acceptance passes. Post-fast-travel saving and population
remain unverified.

Original owned files, recording and user saves remain untouched. Investigation
copies/logs stay private in ignored local storage. Package g,
`OpenNV-fnv-repair-20261017-g-windows-x64`, pins clean source
`92148ee84f3643f1b11b93f90fcd1942bdcb896f`; all 207 file hashes and exported
launcher identity validate, with no owned data. ZIP SHA256:
`2df69df4aaefa394817cdbefd942d8e0d9bf495975d21fb0ac697526d0d82afe`.
Existing New Vegas flat/VR test shortcuts point to g with the original profile;
its save hash is unchanged. PR #210 carries the repair and its checked integration.
Package f remains the historical failed candidate. Begin the next implementation
block from current origin/main on a fresh feature branch.

Cheyenne's saloon portal approach remains unaccepted. Missing SpeedTree decoding,
unsupported packages/scripts, unloaded background simulation, campaign/ED-E,
physical Quest 3, FO3 VR, mods and TTW remain open. Earlier bounded tutorial or
simulator checks do not establish these capabilities. Loading is not instant.

Frame recording is off; only selected diagnostic stills are inspected.
Automatic approval review blocked deletion of superseded packages, older
diagnostics, and this investigation's temporary test sessions/frames. It returned
only "blocked by policy" for the current cleanup. Those local artifacts remain;
recording is off. Do not bypass the rejection.
