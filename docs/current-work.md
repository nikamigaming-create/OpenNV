# Current work

## Active implementation

The user requires all ten mod targets in [mod compatibility](mod-compatibility.md),
starting with JAM then TTW, with full dependency behavior and ordinary flat/OpenXR
gameplay from selected owned folders. The target includes settings, save/load and
cold restart. Publish each block from a fresh `codex/` branch; registration or an
individual feature is not completion. Work without subagents.

All ten have local source packages: 26 archives are downloaded, hashed and
extracted under `D:\OpenNV-Mods`; TTW uses the existing `D:\TTW\Installed`.
The private inventory is `tmp/mod-packages.private.json`. Chrome downloads work
when the download event is awaited alongside the download action.

The launcher setup now covers all ten targets, nested extracted Data folders,
ordered dependencies/patches, transitive masters, alternative EVE DLC
variants and separate saves. Selected folders feed the ordinary live source
owner and survive session restarts. Later loose folders and same-named archives
win without writing to the game directory; active plugin archives also accept
compact dash suffixes. Unrelated retail plugin selections stay out of mod profiles.
The earlier profile casing and mixed TTW engine detection bugs are corrected.

The user additionally required ImageGen artwork, substantially better UI/UX,
additive mod selection and automatic load order with optional advanced tweaks.
The native launcher now uses original generated Mojave art, a searchable library,
independent mod checkboxes and a separate folder inspector. The shared C# stack
planner applies authored masters and reviewed rules. Enabled selections, automatic
mode and manual overrides survive restart, with saves isolated per effective stack.
The combined JAM + TTW + NMC owned stack opens and all three remain enabled in
the native launcher. No gameplay claim follows from these source checks.

The full required repository gate passes, including Release/Debug builds,
formatting/analyzers, contracts, launcher tests and native Godot loading. All ten
owned stacks open, with no missing declared package files; private results are
`tmp/mod-stack-results.private.json` and `tmp/*-layered-audit.private.json`.
This is source loading, not working mod gameplay. The shared interpreter now
executes numeric and typed NVSE assignments and eval expressions against existing
state, with focused cold-state and migration checks passing. JAM's latest source
audit accepts 31 and rejects 21 of 52 scripts; TTW's last source audit had 61
among 1,263 entry-plugin scripts.
These source admission counts do not establish command execution or mod gameplay.
See [NVSE script runtime](nvse-script-runtime.md).
Native launcher checks pass at 1280x900 and 1060x700. All 5,407 NMC winning
textures decode. Two partial
authored mip chains now upload without fabricating extra levels, with exact GPU
byte readback and no resource leak. Pixel presentation remains unverified.
Gameplay routes remain unavailable while implementation/acceptance is incomplete.

JAM's shared runtime now also executes scalar user functions, nested loops,
per-script load/restart queries and main-loop/key callbacks. Process event state
survives scene/save reload with fresh executor bindings; it retains no retired
world delegates. Synthetic recursion, callback mutation and restoration checks
pass. Native Godot frame/mode and physical key-edge checks mutate source-owned
reference slots successfully. The owned JAM execution audit gets past lifecycle
queries and the now-owned UI component commands, exposing the next effect, perk
and render-event gaps. Evidence is
`tmp/jam-events-{contracts.log,native.log,source.private.json,owned.private.json}`.
Full MCM menu/settings behavior is explicitly included in JAM acceptance.
The full required gate passes with the function/event changes; its private log is
`tmp/jam-events-gate.log`.

Block 1 of the [JAM/MCM plan for Luna Max](jam-luna-max-plan.md) is now implemented
in the shared expression, quest, reference and save owners. Numbers, named forms,
quoted strings, concatenation, `$`/`ToString`, typed string arguments/results,
`reference` locals, isolated typed UDF frames, string handles/copy/destruction and
validated cold state all have focused synthetic coverage. The selected owned audit
still reports 461 quest owners, 41 visible unbound summary entries and no fabricated
owned string entries; remaining failures stay visible and are not treated as
gameplay support.

Block 2 of the [JAM/MCM plan for Luna Max](jam-luna-max-plan.md) merged as `9dd4c71`
(PR 48, all checks passed). The shared script-storage owner now reaches INI
float/string reads and writes through selected `Data/Config` precedence with an
atomic profile overlay, plus JIP typed auxiliary float/form/string arrays with
public/private and temporary/permanent lifetimes. Quest and reference execution
share the same storage; permanent state is in script saves, temporary state is
session-only, and New Game clears both auxiliary lifetimes. Synthetic storage and
source-script write/readback, serialized restore, cold restart and New Game checks
pass without changing owned source bytes.

The active `codex/jam-mcm-ui` block now resolves the selected UIO manifests,
MCM start-menu includes and owned MCM/JAM XML through the live source graph. A
source-owned menu-session component store owns scoped tile lookup, indexed paths,
float/string traits, source expressions, component unload and the reached UI
function/command forms in both quest and reference execution. Synthetic organizer
and component probes pass; the owned source audit reaches past `UnloadUIComponent`
and now exposes the next render-event, actor-effect, perk-mutation and parser gaps.
MCM registration/API behavior, Godot menu presentation, ordinary input and actual
JAM gameplay remain open. This is not a working MCM or JAM gameplay claim.

The verified runtime baseline before this block was main `9dd4c71` (PR 48, all
checks passed). The next checked block is MCM registration and option behavior,
followed by ordinary flat/XR menu acceptance. Arrays, full MCM behavior and
ordinary-input acceptance remain open. The plan specifies owners and the checked
publication loop.

## Verified candidate

OpenNV remains an experimental flat/OpenXR playtest. The active implementation
plan and all 36 recovery requirements remain in scope. Work in this task without
subagents. The user must playtest flat and a physical headset before any golden
release; simulator evidence cannot provide that acceptance.

Fresh ordinary flat and Elliott Tate simulator checks repair and recruit ED-E
with previously collected parts. Mobile actor MoveTo now projects onto authored
NAVM; inherited Immobile creatures and ordinary objects retain exact offsets.
ED-E's controller root settles at the Nash floor in both modes, recruitment
completes and Follow is active. The source hover skeleton is unchanged.
A close camera can still crop him; antenna appearance needs reference review.

The preserved second-hostile checkpoint now crosses the source curb in both
modes, kills the additional hostile and resumes Follow. Source navigation is
refined with the actor's actual capsule. Unavailable routes idle and retry;
searches yield under a shared two-millisecond physics-thread budget. Source
mesh bounds accelerate exact triangle projection. A selected route request
measures 2.71 ms versus earlier 52-142 ms samples. This is a finite encounter
result, not all-route or combat acceptance. See [creature packages](creature-packages.md).

The earlier exported encounters verify the Fiend firing/reloading, player
damage, ED-E's first kill, corpse loot and post-damage Stimpak use in both modes.
Source acquisition of Enhanced Sensors is saved; its detection effect and NPC
radio are unbound. Ammo-free embedded weapon recharge, broader tactics,
unarmed NPC fallback, hit/death events, XP and full damage ordering remain open.

## Latest user playtest corrections

Flat Escape and XR Menu/right-stick click open a shared pause menu. The save
browser creates separate manual slots, selects earlier saves and preserves the
previous Continue file before promotion. An in-process main-menu return and
selected-save reload drain source workers and release the retired scene owners.
Ordinary flat saves and Elliott Tate controller menu/save/load/title operations
pass; the XR death screen was inspected in both final eyes.

The user's current Bison Steve save contains zero health. Previously the runtime
had no death/reload presentation and Aid correctly rejected that state. Zero
health now pauses gameplay with a load menu; interaction and manual saves cannot
overwrite a playable Continue file after death. The original file is preserved,
with three earlier genuine checkpoints added privately as selectable slots.
The exported flat executable rejects movement/save/Resume after death and loads
a healthy Primm slot in process. Marker idles now bind the actual equipped rifle;
all twelve source marker clips pass native channel/pose checks for both riflemen.
Fresh ordinary Stimpak use changes flat health 64.94 to 104.54 and simulator
health 161.19 to 200, consuming one item in each mode. Physical acceptance and
source death animation/camera parity remain open.

The two enabled elevated Primm riflemen use authored linked Patrol routes.
They now reach source markers, wait and proceed under native capsule collision.
Marker arrival projects onto nearby source NAVM instead of comparing feet with
an elevated editor marker. Route progress is retained in v18 saves; v17 and
earlier supported saves remain readable. Source idles and weapon conditions use
the same actors. See [patrol and session recovery](patrol-session-recovery.md).
This does not certify the whole roller-coaster loop or all actor packages.

Continue/Load display loading feedback; failures stay visible.
Persistent encounter-zone levels unblock hotel actor admission, including cold
restoration. The selected Primm exterior/12-interior audit passes admission/resource checks for 54 enabled
actors; 48 disabled actors in that saved state stay disabled, including previous
combat deaths. This is not a count of initially disabled source placements.
All 14 enabled hotel NPCs prepare,
and six upstairs NPCs are present in an exported continuation. A missing female
ARMO model now falls back to the source male model and its material fields.

The authored broken-floor walkway exceeds Godot's implicit 45-degree limit.
An explicit 50-degree OpenNV controller policy is shared by flat/XR players,
actors and capsule queries. Both ordinary modes climb it without jumping; XR
also returns down it. Walls, low ceilings and steep slopes remain blocked.
This is a bounded locomotion correction, not a measured retail slope policy.
See [encounter admission and traversal](encounter-admission.md) for contracts,
source references and limitations. User saves remain untouched by these tests.
## Performance and exterior work

Content preparation adapts to process CPU/memory availability: one quarter of
logical processors, clamped to one through four workers, at most two below
8 GiB. This 28-thread/32-GiB Windows host selects four. Exterior/LOD preparation
shares that admission limit, bounded upload queues and cancellation. Scene,
gameplay and physics publication remains on its owning thread. The OS schedules
core types; no hard-coded affinity is applied.

Cached winning settings and shared exterior shader constants improved the
selected stationary flat checkpoint from 15 FPS / p95 80.61 ms to
60 FPS / p95 17.60 ms. A selected safe-renderer simulator continuation after the
navigation changes measured 82 FPS / p95 20.03 ms. Earlier safe XR measured
45 FPS / p95 28.23 ms; these are different samples, not an isolated A/B or
physical-headset performance acceptance. Separate rendering remains an explicit
development option because this Godot build errors during render-thread shutdown
on both D3D12 and Vulkan. See [runtime performance](runtime-performance.md).

Cloud rates use weather wind and the winning speed setting; all 98 declarations
and selected native wind tests pass. Ordinary flat/XR wind bodies move with
independent gusts. A distant body falls below terrain and cold prop persistence
remains open. Streamed NPC source geometry now prepares on bounded workers;
native assembly yields between body parts and publishes only complete actors.
The selected flat walk reduced the largest upload slice from 183.46 to 59.67 ms
and the worst draw interval from 197.09 to 81.33 ms. Rolling p95 and cell commit
did not improve; see the complete measurements in [performance](runtime-performance.md).
A fresh Elliott Tate thumbstick run also completes both cell transitions and
the streamed NPC; its maximum upload is 53.79 ms and last commit 46.63 ms.
Moving LOD, distant water
materials, vegetation/alpha and all eligible actor spawns need further work.
Property-free distant meshes in inspected blocks are water, not missing LAND.

## Deliverables and evidence

The private 2:15 comparison is
`local/recordings/playtest-20260920/OpenNV-flat-vr-companion-showcase-updated.mp4`.
It pairs dialogue, barter, Pip-Boy inventory/illustrations, recruitment, the
corrected first encounter, loot, healing and ED-E outside. Independent ordinary
input takes use normal speed, flat audio and Elliott Tate's left projection eye;
both eyes were separately inspected. The adjacent edit manifest identifies
sources/cuts/hashes. The reel predates the latest indoor placement/curb changes.
New inspected pictures and results are in the adjacent `PLAYTEST-STATUS.md`.
Player melee weapon actions now resolve attack clip suffixes with alternation,
extend reach beyond the player capsule radius, evaluate combat hit cone contacts
with line-of-sight checks, and land authoritative damage and condition wear.
VR crafting and an uninterrupted paired Goodsprings-to-Primm trip remain unproved.

The current publication checks cover Release/Debug builds, formatting/analyzers,
contracts, launcher tests and native loading; all pass. Owned NPC preparation
checks verify exact source geometry/expression vectors, worker execution,
cancellation, complete-body publication and freeing abandoned native nodes.
Private evidence is `tmp/development-lab/stream-npc-publication-*` and
`stream-upload-{before-01,after-01,xr-01}/moving.private.json`.
Selected owned companion,
native follow/curb/combat and exhaustive-versus-bounded projection checks pass.
Private evidence: `tmp/development-lab/companion-publication-*`,
`companion-navigation-bounds.log`, `companion-owned-curb-fixed.log`,
`companion-curb-{flat-02,xr-01}/result.private.json` and
`companion-repair-{flat-02,xr-01}/result.private.json`.
The release manifest identifies the exact packaged commit; no retail parity is
claimed from these checks.

## Next executable work

Implement the actual JAM script, event, UI, animation and persistent-state owners
against the complete dependency stack. Numeric assignments and expressions are
implemented, along with scalar user functions, loops, frame/key event owners,
INI/JIP auxiliary storage and source-owned UI component state. The next bounded
owner is MCM registration and option behavior, followed by actor-effect/perk and
render-event owners reached by JAM initialization. Preserve vanilla expression
semantics and existing saves while adding each capability. Arrays, new-game-only
lifecycle signalling, main-menu callbacks without a world, XR key adaptation and
full dependency behavior still require implementation/evidence.
Only after JAM's ordinary-input acceptance passes proceed to TTW's combined
campaign, transitions and extension behavior. Keep all original campaign and
physical-headset requirements in scope. Prior candidate evidence is
`tmp/development-lab/load-encounter-*`, `primm-population-audit.json` and
`load-encounters-{flat-02,xr-01}`. The release manifest identifies packaged source.
Do not run competing gameplay/performance checks during the user's playtest.

Remaining priorities: unsupported actor scripts/packages and resources, broader
population and building coverage, indivisible material uploads, cell commit,
explicit-save pauses and moving LOD. The per-shot full-save trigger is already
removed; explicit saving remains synchronous. Finish the missing ordinary-input
combat/crafting demonstrations and continuous paired route without replacing
campaign behavior with selected scenes. Preserve the genuine checkpoints below.
- `tmp/development-lab/ede-ready-to-repair-20260920.json`: Nash, Repair 31 and
  three Scrap Metal, two Sensor Modules and one Scrap Electronics collected.
- `tmp/development-lab/ede-outside-before-combat-20260920.json`: recruited follower
  after the real door exit, before combat.
- `tmp/development-lab/ede-{flat,xr}-companion-kill-looted-20260920.json`: genuine
  first-kill/loot outcomes; the XR checkpoint exposes the second encounter.

Private assets, saves, captures and executable observations stay out of Git and
public releases. Recording stays off outside selected visual checks. Keep only
requested deliverables and diagnostics needed for active defects; the private
cleanup-pending list records frame deletion previously blocked by automatic review.
