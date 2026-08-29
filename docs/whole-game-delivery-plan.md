# OpenNV whole-game delivery plan

Status: **active; the complete product is not yet delivered**.

This is the canonical execution plan for turning OpenNV into one asset-free,
data-driven Godot implementation of legally owned Fallout 1, Fallout 2,
Fallout 3, and Fallout: New Vegas installations. It owns priority, milestone state, evidence
requirements, and the active slice. Detailed subsystem truth remains in the linked architecture,
whole-game CELL, actor/creature, compatibility, and evidence contracts.

The plan is durable by rule:

- update **Current baseline** only when retained evidence changes;
- update **Active slice** in the same commit that promotes or replaces a slice;
- never mark a milestone complete from intent, parsing coverage, a launch, or a
  showcase alone; and
- keep unfinished product requirements here even when they are outside the
  current bounded implementation slice.

## Product success definition

OpenNV is complete only when a player can select legally owned installations;
choose Fallout 1, Fallout 2, Fallout: New Vegas, standalone Fallout 3, or the
declared TTW character path; use the retail-faithful front end and character
flow; finish each authored opening; and play the intended campaigns with persistent menus,
HUD/Pip-Boy, people, quests/dialogue, combat, crafting, loot/inventory, world,
audio, rendering, saves, and supported compatibility behavior in flat and
OpenXR modes. The source/build distribution must remain asset-free.

Each campaign owns one authoritative gameplay/save state and three presentation
adapters: Hex Tactical, First Person, and OpenXR. Fallout 1/2 consume their
owned DAT/MAP/PRO/FRM sources directly; Fallout 3/New Vegas consume their owned
ESM/BSA/NIF/DDS/KF sources directly. A Hex presentation of a Gamebryo world is
the real compiled 3D cell under tactical hex movement/camera rules, not an
invented FRM substitute. Optional derived sprites are disposable presentation
caches and never become gameplay authority.

The Hex adapter uses one locally sourced classic Fallout 1/2-style HUD and
Pip-Boy shell across campaigns. It displays each campaign's authoritative
stats, inventory, equipment, quests, crafting, map, and save state; it does not
translate that data into Fallout 1 rules. FPS and OpenXR use the selected
campaign's native-style HUD/Pip-Boy adapters over the same state.

Fallout 4 and Skyrim are stretch profiles, not part of the current
launcher-ready denominator. The locally owned Fallout 4 VR installation provides the base
master/DLC/BA2 graph plus a separate VR master and VR-specific archives; those
overrides remain a build/profile adapter instead of being assumed identical to
flat Fallout 4. The locally owned Skyrim VR installation likewise provides the
base game, Update, all three official DLC masters, `SkyrimVR.esm`, and its VR
archive as a distinct adapter input. Oblivion is tracked as a sibling
Gamebryo-family port, not as a Fallout campaign or save path.

“Complete” requires every row in **Product completion matrix** to have direct,
retained evidence. A green representative scene, parsed record inventory,
gallery, screenshot, video, or narrow vertical slice cannot substitute for that
scope.

The end-game presentation artifact is a scripted engine capture, not proof by
itself: each game moves from front end and character opening into the same live
slice in Hex, FPS, and a short OpenXR view. Produce a 16:9 master with center-safe
titles and action so it remains readable on mobile; unsupported modes never
appear as staged or composited stand-ins.

## Non-negotiable execution contract

### Owned data and configuration

- Retail plugins, archives, meshes, textures, animation, audio, XML, saves, and
  executables are read-only inputs. Generated derivatives stay in ignored,
  disposable local caches and never enter Git or a release.
- Fallout identities, placements, transforms, enable state, item values,
  dialogue, quest stages, packages, animation, materials, lighting, weather,
  effects, LOD, collision, navigation, and portals come from the effective
  owned record/resource graph. Recognizable content is never hand-placed.
- Executable source contains no content FormIDs, scene coordinates, pose
  corrections, actor-specific camera fixes, or guessed retail values. Stable
  selection identities belong in versioned recipes or generated manifests.
- Non-retail policy lives in `runtime/config/open-nv-runtime-v1.json`, with
  provenance and parity status. External-format and mathematical constants are
  named contracts. The source-constant audit remains blocking.
- Unknown layouts, commands, precedence, references, or resource semantics fail
  closed. A proxy, generic fallback, silent omission, or visually plausible
  substitution is not progress toward parity.

### One runtime, one state, one build authority

- `origin/main` is the canonical source authority. Flat and OpenXR use one
  gameplay simulation and one save/profile state; input and presentation are
  adapters only.
- There is one current runtime build. Development tests may create disposable
  cache/output directories, but the repository may not accumulate exported
  builds, ZIPs, copied runtimes, or numbered “latest” packages.
- Packaging is a release milestone, not an everyday implementation step. Until
  that milestone, validate the checkout/runtime directly and replace temporary
  build output rather than archiving it.
- A profile records its plugin stack, character path, configuration, compiler,
  and save compatibility identity. A save never silently changes between base,
  JAM, or TTW worlds.

### Fidelity and proof

- Retail parity is a matched-state measurement: same effective data, loaded
  state, time/weather, camera, animation, equipment, and simulation point.
- Fidelity work uses fixed-camera differentials, telemetry, source hashes, and
  named remaining deltas. Repeated tuning without a measured failing owner is
  not a fidelity pass.
- FNV/JAM screenshot or video work follows the sibling canonical background
  capture runbook and recipe catalog. Retail and Godot/OpenMW run sequentially;
  no Windows app control, focus, click, `SendInput`, or reused evidence
  directory is allowed.
- A presentation video is derivative evidence. Native frames, telemetry,
  source identity, report schemas, and validators remain authoritative.

### Publication hygiene

- Implement one coherent bounded slice, run every selected promotion gate,
  squash it to one reviewable commit, and atomically update the working feature
  branch and `origin/main` when both are fast-forward safe.
- Keep local `main` fast-forwarded when its worktree is clean. Do not overwrite
  unrelated work in another worktree.
- Prefer an existing PR only when review is actually needed. Do not accumulate
  redundant PRs, branches, proof archives, or build packages.
- Every promoted slice leaves the worktree clean and updates this plan when its
  evidence or next owner changes.

## Evidence state vocabulary

| State | Meaning |
| --- | --- |
| `proven` | The declared bounded behavior passed all selected gates and has retained evidence. |
| `partial` | Some required behavior is proven, but the milestone's full denominator is not. |
| `pending` | Required implementation or direct evidence is absent. |
| `blocked` | A named source semantic or external acceptance dependency prevents promotion; no substitute is used. |

The narrower promotion labels remain: `transported`, `rendered`,
`interactive`, `parity-reviewed`, and `headset-accepted`. They are not
interchangeable.

## Current baseline

| Surface | Current evidence state | Authority |
| --- | --- | --- |
| Owned installation import and disposable cache | `partial` | [Architecture](architecture.md), [clean-room boundary](clean-room.md) |
| Owned Fallout 1 menu/character/movie/V13ENT route | `interactive` in Hex/FPS; OpenXR simulator input passes, but launcher enablement and physical-headset acceptance remain open | [Multi-game first slices](multi-game-first-slices.md) |
| Owned Fallout 2 character/Temple/Arroyo Caves route | `interactive bounded Hex launcher slice`; premade Take plus source-backed Modify/Create for name/sex/age/exact SPECIAL, PRO-linked sex-correct AA idle and directional AB walking at tile 28707, and version-2 atomic male/female cold restore work; tag/trait editing, other animations, campaign-wide persistence, scripts/actors/combat/inventory, reciprocal exits, full campaign, FPS/OpenXR, and parity remain pending | [Multi-game first slices](multi-game-first-slices.md) |
| Owned New Vegas menu, intro, character creation, and Doc Mitchell opening | `interactive`; checkpoint/resume reaches the stage-200 open-world-ready state, while uninterrupted full-campaign continuity and visual parity remain pending | [Owned opening campaign-state contract](evidence/fnv-owned-opening-campaign-state-contract.md) |
| Doc Mitchell house/Goodsprings exterior/saloon ordered route | `interactive` bounded forward flat route from completed stage-200 owned Continue; configured input traverses both XTEL links, and campaign save v5 cold-restores saloon CELL `00106185`, container remaining counts, and the player transform; player deposits, reverse traversal, neighboring active-set streaming, integrated OpenXR acceptance, and Sunny behavior remain pending | [Normal-menu Goodsprings route](evidence/fnv-normal-menu-goodsprings-route-contract.md) |
| Owned Fallout 3 menu, intro, and CG00 sex/name/appearance through persistent stage 62 | `interactive frontend`, not a playable presentation; later state contracts validate, while authored triggers, dialogue/KF, actors, Vault 101 scene compilation, and world play remain pending | [Multi-game first slices](multi-game-first-slices.md) |
| Goodsprings saloon plus one exterior portal, gameplay, and cold reload | `interactive`, visual parity pending | [Goodsprings linked-world contract](evidence/fnv-goodsprings-linked-world-contract.md) |
| Whole official CELL/child denominator and compile plan | inventory `proven`; runtime/parity `pending` | [Whole-game CELL parity](whole-game-cell-parity.md) |
| Whole official actor/creature denominator | inventory `proven`; runtime/parity `pending` | [Whole-game actor and creature parity](whole-game-actor-creature-parity.md) |
| Materials, FaceGen/LIP, and bounded OpenXR paths | mixed `partial` | [Material contract](evidence/fnv-retail-material-shader-contract.md), [FaceGen animation contract](evidence/fnv-retail-facegen-animation-contract.md), [OpenXR contract](evidence/openxr-runtime-contract.md) |
| Retail HUD/Pip-Boy | source contract/runtime shell `partial`; complete tile behavior and parity `pending` | This plan |
| Full campaigns | `pending` | This plan |
| JAM and TTW | TTW runtime support is absent; JAM is dependency- and portable-semantic-gated with bounded JVS sprint and JBT time-dilation semantics transported, while both launcher routes remain disabled | [Mod policy](mods.md) |
| Public playable package | `pending` | [Release policy](nightlies.md) |

The current source baseline is whatever commit `origin/main` resolves to. The
plan does not embed a moving commit hash; each evidence report and release
manifest records its exact source revision.

## Active slice: normal launch menu through playable Goodsprings

Priority: **P0 — next implementation owner**.

Objective: prove one ordinary, uninterrupted campaign route without changing
pipelines: launch at the real front end, choose New Game, complete the accepted
Doc flow, expose the player's real post-opening inventory in the retail
HUD/Pip-Boy, traverse Doc Mitchell's authored XTEL exit, stream the required
Goodsprings exterior active set and LOD, enter the saloon, observe authored
enabled Sunny, save the active location, restart, and Continue from that exact
state. The narrower completed-save subroute now passes through the owned
Continue button, configured flat input, both forward XTEL links, saloon
active-CELL persistence, and a fresh-process v4 Continue restore. That does not
complete this objective: uninterrupted New Game-to-saloon play, reverse
traversal, required load/unload streaming, authored Sunny behavior, integrated
OpenXR, and the remaining UI/presentation gates remain blocking.

Implementation order:

1. Promote the normal front-end route. New Game must enter the authored opening;
   Continue/Load must reflect and restore the canonical save. The end-to-end
   proof may not use `--new-game` to bypass the menu. Campaign admission now
   joins the nested opening inventory, equipment/weapon metadata, and player
   transform to the ordinary gameplay fields before enabling Continue; later
   completed-campaign saves update both representations from the live session.
2. **Partial:** compile the owned HUD/Pip-Boy menu, XML, font, image, inventory, equipment,
   quest, map, and control sources into neutral versioned UI contracts. Preserve
   the owned XML input format for stock/JAM/TTW compatibility; do not bake a
   Doc-specific Godot layout. The current contract binds HUD/STATS/ITEMS/DATA
   document closures, exact XML hashes, selected owned fonts and prepared
   textures, the Pip-Boy background, and selected HUD/ITEMS/DATA source
   rectangles. STATS currently reuses the verified ITEMS frame because its
   source rectangle depends on unsupported Gamebryo expressions; the remaining
   tile/data-binding semantics are still open.
3. **Partial:** add one generic UI model/controller over authoritative campaign state. It
   must support the stock HUD plus Pip-Boy status, items, data/quests, and local
   map surfaces; flat and wrist presentation consume the same model. The current
   controller reads that one snapshot, gates the gameplay UI from the authored
   Pip-Boy control state, and renders the owned fonts/background and selected
   reference-canvas rectangles in flat mode. The wrist currently shows only a
   status view from that shared snapshot; complete stock interaction and pixel
   parity remain pending.
4. Join the stage-200 inventory/equipment FormIDs to the ordinary gameplay
   inventory and visible first-person presentation. Equip, holster, use, drop,
   and save/reload operate through one item state path.
5. Resolve Doc Mitchell's exit `DOOR -> REFR -> XTEL -> destination REFR ->
   CELL/worldspace` chain from the effective plugin stack. Stream the destination
   and required persistent/exterior neighbors in one coordinate contract.
6. Load authored LAND, NAVM, collision, references, actors, vegetation/LOD
   outcomes, lighting, weather, and enable state for the required Goodsprings
   active set. Unsupported semantics remain explicit blockers.
7. Traverse the door with player/projectile continuity, ground the player and
   actors through authored collision/NAVM, continue normal controls, save in the
   exterior, exit, and cold-reload the exact state.
8. Run a matched retail/Godot review for the front end, interior handoff, door threshold,
   exterior arrival, HUD, and each Pip-Boy panel. Record remaining visual deltas
   without weakening gameplay acceptance.

Blocking exit gates:

| Gate | Required result |
| --- | --- |
| Front end | Normal New Game and Continue/Load routes drive the same campaign/save owners; no command-line gameplay bypass is counted. |
| Source graph | Exact menu/XML and XTEL/CELL/worldspace/resource identities, hashes, and zero ambiguous joins. |
| UI | Stock post-opening inventory, equipment, quests, status, map, and controls are data-derived; no proof-only items or layouts. |
| Streaming | Required active CELL/persistent set loads and unloads deterministically with no missing-source or hand-authored placement. |
| Grounding/collision | Player and actors stand on authored ground; door, static collision, NAVM, and projectiles agree through the transition. |
| Persistence | One save preserves character, inventory/equipment, quests, references, door state, active CELL, and player/actor transforms across a cold process. |
| Presentation | Native frames pass missing/blank/stretched/alpha/LOD gates; matched differentials retain every unresolved lighting/material delta. |
| Flat/OpenXR | Shared outcomes pass configured desktop input and XR software layout; physical headset status remains explicit. |
| Repository | Tests, source audit, format, Debug/Release, owned-data import/reuse, diff check, clean commit, and asset-free tree pass. |

Completing this slice does not complete the whole-game goal. It establishes the
first campaign-continuous route on which later systems must run.

## Milestone sequence

### M0 — Proven owned-data foundations (`partial`, retained)

Keep the existing ESM/BSA/NIF/DDS/KF/LIP/TRI/LAND, opening, Goodsprings portal,
save, flat/OpenXR, and corpus gates green while replacing bounded special cases
with general capability owners. Do not delete working acceptance routes merely
because a general compiler is being introduced.

Exit: existing evidence contracts remain reproducible from a fresh legal cache
and the active slice passes.

### M1 — General streamed world (`pending`)

Implement every required CELL child/base capability through the partitioned,
content-addressed compiler and validator. Close partial/default LAND semantics,
general NAVM, persistent references, enable parents, XTEL portals, water,
SpeedTree/vegetation, world LOD, occlusion/culling, weather/climate, effects,
audio emitters, and save-aware active-set streaming.

Exit: representative interior/exterior/worldspace/portal classes pass first,
then every effective CELL review row has direct runtime and source closure. No
CELL is declared working from parse coverage alone.

### M2 — Authoritative gameplay and quest simulation (`pending`)

Generalize inventory/equipment, containers, barter, dialogue, quests,
objectives, globals, scripts/commands, packages, crime/faction/reputation,
companions, combat, damage, healing, projectiles, explosives, interaction,
physics, time, and deterministic save/load. Implement only behavior required by
owned records and observed contracts; do not add speculative managers.

Exit: campaign routes execute authored state changes and cold-reload them with
record-by-record command coverage and no proof-only mutation path.

### M3 — Actors, creatures, animation, and AI (`pending`)

Close all actor/creature appearance outcomes and placements: template/leveled
resolution, FaceGen/head materials, skin palettes, equipment, weapon hand and
holster sockets, layered KF/root motion, idles, locomotion, dialogue LIP,
expressions, ragdoll/death, creatures, and package/combat AI. Feet, hands,
weapons, and collision share one published pose; no actor floats, slides,
T-poses, neck-mounts a weapon, or receives a per-character correction.

Exit: every actor/creature corpus outcome and placement row has passing source,
runtime, animation/state, matched visual, and contextual grounding evidence.

### M4 — Retail presentation closure (`pending`, continuous lane)

Materials and rendering advance alongside every prior milestone; this milestone
closes the remaining denominator. Preserve shape/material identity and implement
alpha, depth, vertex color, normal/specular/environment maps, external
emittance, image-space modifiers, HDR/color grading, shadows, fog, atmospheric
dust/particles, water, terrain layers, vegetation, sky, lighting, weather/time,
effects, decals, and retail camera/projection behavior.

Exit: the declared thirteen-area set and exhaustive required CELL/actor shots
pass matched native-frame metrics and human review. A darker, cleaner, or more
cinematic substitute does not pass if it differs from retail.

### M5 — Complete UI, controls, and platform presentation (`pending`)

Finish front-end/profile/load/save/settings, HUD, dialogue, barter, containers,
V.A.T.S., terminal, lockpick, crafting, repair, character progression, world and
local maps, Pip-Boy XML/menu compatibility, subtitles, notifications, and all
other campaign UI. Flat and OpenXR share state; OpenXR gets readable stereo-safe
world/wrist presentation and physical interactions without changing outcomes.

Exit: every stock UI route has deterministic flat acceptance, required XR
software acceptance, and physical headset acceptance where applicable.

### M6 — JAM and TTW compatibility profiles (`pending`)

Treat compatibility as data and behavior contracts, not bundled mods or native
DLL loading. Resolve effective plugin order, XML/menu overrides, records,
scripts, xNVSE/JIP/JohnnyGuitar/kNVSE command semantics, animation events, and
save/profile identity. Validate JAM modules independently and together. TTW is a
separate character path with its required owned Fallout 3/New Vegas data,
Capital Wasteland start, transition, and combined-world persistence.

Exit: declared base, JAM, TTW, and TTW+JAM matrices complete their authored
routes, UI, commands, animation, save/reload, and no-control capture gates.
Unsupported extensions remain named, not silently ignored.

### M7 — Full campaign and whole-denominator acceptance (`pending`)

Run start-to-ending campaign automation plus side systems over the ordinary
runtime, then close the complete CELL, actor/creature, UI, command, item,
quest/dialogue, audio, and compatibility ledgers. Sampled showcase success is
insufficient; every declared denominator row needs its required evidence.

Exit: all product-completion rows are directly proven, all blockers are zero,
and flat/OpenXR gameplay results are equivalent. Physical headset gates must be
current, not inferred from the simulator.

### M8 — Asset-free release (`pending`)

Only after M7, create one release candidate from a clean `origin/main` commit.
Build the bundled content helper and runtime on each supported platform, perform
a fresh legal import and cache reuse without a source checkout or Python,
validate licenses/notices, scan the staged tree and final archive for owned or
derived assets, and publish source revision plus package hashes.

Exit: one reproducible, validated release per supported platform; no historical
development builds or proprietary cache content in the repository or package.

## Product completion matrix

| Requirement | Completion evidence | Current state |
| --- | --- | --- |
| Legal import and profile identity | Fresh and reused import for every supported owned-data/profile stack; complete source/output hash closure | `partial` |
| Front end and character creation | Matched menu flow, profile choice, New Game/load/settings, appearance/SPECIAL/skills/traits, Doc opening and cold resume | `partial` |
| World streaming and LOD | Every effective CELL/child capability, active-set transitions, portals, persistent refs, LAND/NAVM/water/vegetation/LOD/weather/effects | `pending` |
| Gameplay simulation | Complete required item, inventory, equipment, interaction, quest/script/dialogue, AI, combat, physics, time, and save behavior | `pending` |
| HUD and Pip-Boy | Stock XML/menu-driven HUD and all Pip-Boy/UI panels over authoritative state, plus flat/XR interaction | `pending` |
| Actors and creatures | Every appearance/placement/state/animation/equipment/grounding/combat row passes | `pending` |
| Materials and lighting | Matched shape/material, alpha, terrain, sky, water, lighting, weather, particles, post-processing, and camera evidence | `pending` |
| Audio and dialogue | Music, ambient/placed sound, voice/LIP, radio, effects, dialogue timing, subtitles, and persistence pass | `pending` |
| Base/JAM/TTW compatibility | Declared command, UI, animation, plugin-stack, route, and save matrices pass without bundled third-party content | `pending` |
| Flat and OpenXR | Shared deterministic outcomes; desktop gates, XR software gates, and current physical headset acceptance pass | `partial` |
| Full campaign | Start-to-ending and side-system routes pass with complete ledgers and cold reloads | `pending` |
| Distribution | Clean source, packaged helper/runtime tests, asset-free scan, notices, reproducible hashes, and no build debris | `pending` |

## Gate required for every promoted slice

Select every applicable row; selected rows are blocking.

1. Declare source/plugin/profile hashes, exact denominator, supported behavior,
   unsupported behavior, and intended promotion label.
2. Add synthetic parser/compiler/runtime tests for success, malformed input,
   ambiguity, unknown semantics, and deterministic output.
3. Compile a fresh immutable local artifact and independently validate schema,
   configuration, source graph, output hashes, and complete file accounting.
4. Load the ordinary Godot runtime and validate entity identity/count/state; a
   test-only scene or mutation path cannot be the sole evidence.
5. Exercise configured player/engine input, persistence, cold process reload,
   and flat/OpenXR shared outcomes where the slice changes gameplay.
6. Capture matched native retail/Godot evidence where presentation or behavior
   parity is claimed; retain telemetry and explicit remaining deltas.
7. Run all Python tests, compile checks, source-constant audit, C# Debug and
   Release builds, `dotnet format --verify-no-changes`, relevant Godot gates,
   and `git diff --check`.
8. Confirm no retail/derived assets, caches, saves, captures, exports, ZIPs, or
   unrelated user changes are staged.
9. Commit the coherent slice once, publish fast-forward/atomically, verify
   remote refs and PR state, and leave both working trees clean.

## Completion audit

Before calling the product done, enumerate every explicit requirement in this
plan and join it to current retained evidence. Mark missing, indirect, sampled,
stale, or scope-mismatched evidence as not complete. Re-run the full campaign,
whole-denominator, compatibility, packaging, and physical headset gates from the
candidate `origin/main` revision. Completion is valid only when no required row
is `partial`, `pending`, or `blocked`.
