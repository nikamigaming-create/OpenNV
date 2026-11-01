# Product status

Updated 2026-10-31. User priority: **retail behavior and presentation through
Goodsprings to the Strip, sustained native 3840x2160 at 60 FPS indoors and
outdoors, and physical XR acceptance with a separate 144 Hz target**.
FO1 remains an incomplete classic preview with selected movement, inventory,
equipment, door and save flows. Complete campaign execution and 1:1 3D art are
unverified. See [current-work.md](current-work.md) for the active owner.

The published tester build 743aa164 failed user acceptance: severe outdoor
slowdown and visibly unresponsive coyote combat. Do not call it tester-ready.
The current work is substantial retail recording, Godot playback and shared
system repair across all areas and applicable actions. A diagnostic replay
reproduced severe slowdown, but its checkpoint is unmatched and recording
amplifies the cost. Scene parity and faster-than-retail performance remain open.
See current-work.md for the measured limits and active repair batch.

The current unpublished gameplay batch verifies ordinary weapon changes, VATS
damage and ammo breakdown with cold persistence. Repair-kit cold reload retains
three kits, the same rifle at 37.3% condition and one completed script receipt.
The ordinary route reached Jean Sky Diving; F5 and cold reload pass after the
Sandbox Eat repair. Diagnostic player-only authored arrivals with matched
yaw/time cover Primm, Nipton, Novac, Boulder City, McCarran and Freeside North,
with independent save/AI/weather and no travel or parity certification. Freeside
North recovered after a loading stale-state timeout; responsiveness remains
open. Ordinary McCarran gate `1287f5` now transfers `0dac34` -> `148dc4` in
13.813 seconds, but the courtyard frame has severe exposure mismatch and 15
reference divergences. The six-minute moving 4K run averaged 51.2097 FPS
(first minute 57.91, last 42.81). A later indexed 179.468-second segment averaged
55.2188 FPS; retail's 59.6318 FPS is not a matched-route comparison because its
camera stopped responding after waypoint 32. Candidate waypoint 122 at the
deadline does not establish ordinary Primm arrival. The NAVM index gate preserves
the owned route's 228 waypoints and projection errors. PR341's initial 115-file
revision `e71b60da` passes all five CI checks but remains unmerged after new VATS
smoke failures. Additional TileImage RGB255 defaults, beard unset-index handling
and reviewed navigation-query reuse pass the full gameplay-visible-final gate;
the owned UI audit passes XML/runtime override checks with `ownedImages=1`.
Live recovery of missing actor `0f570d` remains pending. VATS labels render;
selecting torso at 54.704063% queues and completes two shots, spending 70 AP,
changing ammo 5 -> 3 and target health 60 -> 31.402279 without errors or pending
publications. Both physical impacts hit the head despite torso selection;
retail outcome parity remains open. F5 checkpoint
`58154d0fcad24219a19dbe183ca91783` cold-loads in PID21820 with ammo 3, weapon
condition 0.2699999, target health 31.402279 and no opening error. Earlier 0% on
the default leg did not establish a general mask failure; measured fragment
visibility/native correspondence remains open. Prior cold proofs remain valid.
The fresh publication full gate is running in `gameplay-publication-final-gate.log`;
no further gameplay source edits are planned, and no live performance gain is
claimed. The expanded source also repairs quest failure, actor
values, equipment selection, source VATS layout and exact GPU mask reduction.
The regional imposter packet requires actual regional temporary-CELL/script
attachment and remains private, not integration-ready. Sustained native 4K60,
matched-state parity and broad location/action coverage remain unverified.

The checked PR339 batch completes the ordinary Doc-house exit after shared idle
condition and Sandbox-phase repairs. An enabled Pip-Boy flashlight survives a
committed save and cold restore; a later outdoor save retains the disabled light
and loads successfully. The Stats aid shortcut visibly consumes a Stimpak,
restores health and dims when no longer useful. Source compass offsets, CRT
composition and world-map extent/icon bindings are implemented; exact pixels and
the native local-map pass remain open. These selected results are not whole-game
or sustained-performance acceptance.

The local batch now completes Cheyenne's resident Accompany door transfer and
persists her enabled exterior placement with the source door receipt. Both games
have also walked from the saloon to Doc Mitchell's exterior door. Static managed
animation startup and saved implicit-loop correction remove the observed pool
bulb pulse; absolute brightness still differs. Fresh interior save/load restores
the exact player position at a capture-off 60 FPS snapshot. Cached follower
execution, NPC Follow/Accompany and broad scene/action parity remain incomplete.
The final required gate and selected 46-file, 3,465-block owned audit passed.

The current source repairs creature movement beneath grazing poses by sharing
source-priority root accumulation with the displayed animation. Three paired
bighorner takes completed; all205 sampled stationary grazing/Forward intervals
had zero body travel. Exact magnitude and full skeleton parity remain unverified.
Grass now renders its texture and selected atlas-edge fragments are absent;
final grass color remains under repair. A fresh outdoor save
cold-loaded with exact saved position. The full required runtime gate and44-file,
3288-block owned animation decode passed. Broad creature, room, audio, combat and
campaign coverage remain open; these checks do not establish tester readiness.

The current Release work includes owned Idle fallback, compass strip,
regional weather caches, dedicated F5/F9 save identity, source resource reuse,
and NPC furniture/package repairs. Ordinary quicksave/move/quickload and
isolation from a later menu save passed. Both games completed Sunny's bottle
lesson; OpenNV followed her to the well and now advances through stages 40/45.
An ordinary fight killed both well geckos with player survival. Corpse looting
and post-combat quicksave/move/quickload restored exact position and inventory,
with recovery to 57+ FPS in 11.625 seconds. One reloading recipe consumed its
input and produced the authored materials. These are bounded gameplay cases.

Bounded native-4K capture-off observations reached approximately 59â€“60 draws/s
on outdoor movement, an interior sequence and the stationary well scene. These
are not long-run combat or whole-game acceptance. Restored base health now
matches the qualified 220-health source inputs. Projectile rigid-body impulses,
XTEL approach classification and frame-paced loading are implemented; actual
prop-hit and remaining route checks are open. Shared melee knockback now responds
to real gecko hits with no held movement keys. The subsequent two-kill fight
ended at 170 health and quicksave completed. Paused menu saving also committed
after two original windmill voices finished, without resuming gameplay. Native
cold airborne motion retains velocity differently from the current rate/time
snapshot; that persistence lane remains divergent. The later stationary
57.482/59.547 FPS windows had retail presentation stopped and do not qualify as
simultaneous comparisons. Scene completeness, broad combat and tester acceptance
remain open.

Immediate work is the requested live retail/Godot comparison. A local compositor
preview removes continuous CPU frame copies. Godot now confirms native 3840x2160
viewports and reports 60 FPS in the loaded outdoor scene. The unlocked desktop
shows both games. Bounded GPU footage with separate audio now covers a matched
time/weather setup, turns, road movement and Pip-Boy; camera drift and capture
extent/cadence remain explicit. The consolidated source repair batch is in
repair-batch.md. This does not close 4K60 acceptance or combat failures.

Two local candidates completed the same 183-delivery diagnostic tape with capture
off. A roughly 29-second unpaused window at identical sampled player position
improved from 21.77 to 59.52 draws/s at flat 1280x720. The candidates include
preserved essential-lifecycle work; later routes diverge and actor/world state
equivalence remains unproved. Full runtime and selected owned navigation/speech
checks pass. This is neither whole-route acceptance nor faster-than-retail proof.
Private retail input/capture qualification is active before broader replay.
The prior retail checkpoint originated in a COC-created diagnostic fixture;
normal campaign initialization was unproved. A new ordinary New Game reached
Doc's handoff/exit, radio playback and a visible world map and produced a normal
quicksave. Its questionnaire input segment failed its receipt timing bound and
must be re-recorded; continuous campaign timing acceptance remains open. Earlier
fixture clips remain isolated diagnostics. Native commit-clock recording and
exact harness binary provenance are being qualified before further replay.

The checked repository baseline is PR #336, a8c78dc1. Its shared runtime repairs
passed local and hosted checks; current-work.md records the latest live evidence. Earlier PR #334, f6141664,
passed local/owned gates and all five hosted checks and implemented shared mutable
FormLists and behavioral AV pools, persistent additions/modifiers, both parsed
script hosts and live inventory/equipment/hit/weapon/appearance consumers.
Focused headless contracts, selected owned audit, the full required gate and diff
check pass. The actual GRA list prefix stops explicitly at SetEssential;
Lonesome Road stops at unowned AddItemToLeveledList. Neither quest is claimed complete.
Source definitions remain immutable; unknown state owners still fail explicitly.

Actual immutable export b2a220ad ran a fresh New Game through recorded Doc speech,
Reflectron, ordinary house traversal and Vigor allocation/acceptance. VCG01 reached
80 and Doc physically occupied the psychology chair. F5 saved fresh progress,
SHA256 73A1248C07B3FD0FF4B4832C5350D0D8075F8993DCDE20B8A3270BF086E9E930;
normal close disposed source owners. Six requested screenshots are retained at
3840x2124; no 4K, campaign or matched-retail claim follows. Old 02ABC remains intact.

The experimental package's selected checks did not establish tester acceptance.
Full parity/4K60/physical XR144 remain open. Pending shared actor-state work
includes mutable base flags and essential knockdown/recovery/query/save,
rather than a successful SetEssential command with missing lifecycle behavior.
SCDA instruction execution and general runtime reference identity/allocation/
lifetime/persistence remain absent; source parsing is not a VM.

Packaged build c6065970 continued the healthy stage 110 slot through the real
Pip-Boy handoff, completed Doc at stage 200, saved and exited to Goodsprings.
Its later Sunny continuation exposed background Eat/ambient-dialogue save blockers.
That earlier 1014.4-second private video separately verifies the typed visibility
handoff. Candidate 4 and its newer checkpoint below supersede that continuation.
Horizontal sky seams, sustained 4K60 and matched retail parity remain unresolved.

Packaged candidate 4 (743aa164) continued the Saloon save through Sunny dialogue,
bottle shooting, player gecko kills, incoming damage, wells, real corpse loot and
both ingredient harvests. A new ordinary F5 saved HP 94 and the current quest/item
state; a new process on the identical package restored them. The 3224.833-second
gameplay take and 331.033-second cold Continue take are separate, private,
3840x2124 video-only recordings. Campfire cooking was not performed in this run.

Standalone Eat and mixed Topic/Conversation INFO corrections
pass Debug, focused contracts and selected owned audits. They retain authored
scope/food, real package/chair state, independent retry clocks and consumed meal
receipts. Actual ambient INFO 15a548 reached audio completion; later saves passed.
The final required publication gate passes; isolated physical Eat handoff/meal
playback remains unverified. Unsupported search, whole-cell wandering and exact-reference food
acquisition stay visible; source decoding is not evidence of those behaviors.
Additional reached limits include creature Find packages, property-free LOD
materials, LIGH flag 0x8 and ragdoll joint ownership. Repeated combat health values
for the settler/gecko pair were authored script protection, not a damage reset bug.

The merged batch adds source-bound Sleep using existing travel/portal/bed
owners, exact outgoing package-query retirement, sole marker history restoration,
observed marker procedure phases, typed unchosen-meal failure capture and
qualified forward inventory traversal. Selected native/contracts and owned
Sleep/source-food audits and the merged publication gate pass. Missing inventory provenance, unknown insertions
and duplicate direct-food order remain explicit. Fault persistence does not
establish meal behavior or permit fabricated ingestion.

Actual campfire cooking consumed BrocFlower/XanderRoot and produced Healing
Powder 1. F5 command 263 completed at 10:19:05.997 UTC, HP 99, save/cleanup errors
null and healthy owners. The canonical checkpoint has VCG03 objectives 10/20/30
complete and 40 displayed/incomplete. Integrated export `92183296` Continued that
cooking save successfully, with the item retained and no capture blocker.
Marker 10a72b publishes without its earlier error; Sunny's selected Sleep 176ad3
began native travel and handed off on schedule without a surviving error.
Physical bed entry/wake and actual multi-food meal playback remain unverified.
The earlier two-food exception did not recur before saving; its live fault
capture is unverified beyond selected synthetic contracts. Final inherited-IDLE
phase behavior has selected contract proof and was added after the 921 run.

Objective 40 follow-up toward Sunny/Trudy remains unfinished. The bot kept
planning back toward the elevated rock corridor; manual detours traversed parts
of the area, without establishing physical impossibility or a new game blocker.
Normal quit disposed source owners and preserved the successful cooking save.
The current d4c408 checkpoint supersedes those older launch states; historical
cooking saves remain preserved without invented idle history.
Final-phase export dcf66477 also Continued the saved Healing Powder successfully.
A later actor 157c8f transition exposed bound HeadTrack/face-morph scalar snapshot
support as the next saving owner; no F5 overwrote the cooking checkpoint. Normal
quit disposed source owners. PR #330 also fixes native query temporary ownership
found by hosted CI: focused allocation checks and the fresh full required gate
pass with the strict assertion unchanged. PR #330 completed checked publication; typed-scalar transition support subsequently merged in PR #331.

The merged patch adds explicit source-text timing for audio/LIP-absent responses,
authored furniture endpoint approach and source-qualified Sandbox occupied/exit
procedure codes. Selected retained-source and compact contracts pass; actual
floor activity reselection/re-entry and forward saving no longer report the
GetUp failure. The save retains one occupied floor actor and one lawn-chair
approach; physical lawn-chair entry remains unverified. The full mandatory gate
and diff check plus all five hosted checks passed; PR #328 is merged.
INFO 17af3d was not encountered in the latest run; its new timer has source and
synthetic proof, with actual playback and matched retail timing unverified.

Both formerly missing NPCs 104f03/104f0a publish after Continue; source raking
runs and retires without a missing-reservation error. The lawn-chair mask case
is grounded in actual FURN 153553/NIF data and a synthetic regression; the final
route saved without the prior blocker, but its physical chair entry is unverified.
Earlier integrated ordinary VATS selection/cancel and two hostile-gecko misses
completed real shots/ammunition/AP effects. Hit/damage and full VATS acceptance,
an ordinary complete Caravan match, retail pixels, native 4K60 and physical XR
remain unverified. Active SayTo saving and creature Find/Food remain explicit
missing owners. No campaign or parity completion is claimed.
Classic reconstruction is parked; FO3 and classic work are preserved. Earlier
merged feature checks below describe bounded evidence, not validation of every
current branch change. The user's physical FNV VR playtest remains failed.
See [implementation-plan.md](implementation-plan.md) for the active delivery plan.

| Area | Current state |
| --- | --- |
| Terminals, lockpicking and hacking | Accepted manual/Force lockpicking connects admitted keyless DOOR/CONT activation to input, unlock/XP and resident activation. Locked TERM activation connects owned puzzle generation, selection, lockout admission, XP/statistics and delayed contents. Unsupported ownership, scripts, histories, variants and effects remain explicit. Ordinary campaign, full presentation and XR acceptance remain open. |
| All skills | All 13 NV skill values have a shared player owner. This does not establish complete mechanics: progression, modifiers, dialogue, crafting, combat and access checks must work through ordinary play. The user explicitly requires every skill. |
| Flat UI lifetime | Loading tips now retain borrowed shared font atlases. Normal cold Continue, an exterior/interior/exterior door cycle and F5 pass without the former disposed-image HUD/message failure. HP/AP, reticle and activation text draw. Full runtime checks pass. |
| Flat interaction recovery | An aimed source failure no longer permanently disables HUD updates or later valid prompts. Merged PR #249 uses the local lock first, otherwise its linked side's actual lock. Source/key, dynamic mutation and both cold orders pass. Ordinary walking reaches the affected door with visible E Open / Door / Locked; E respects its real lock, F5 commits, and separate-process Continue, ordinary re-aiming and another F5 pass. Full local and hosted runtime checks pass. Lockpicking, locked traversal and retail parity remain open. |
| Flat dialogue camera | Shared speaker focus and furniture pitch reset pass owned camera checks, a fresh-process questionnaire visual check and the full runtime gate. Normal play completes the questionnaire, tag skills and traits, exits the couch and saves at stage 110. Dialogue zoom/timing and matched retail framing remain open. |
| Flat object handling | Grab uses source dynamic bodies, weight limits and bounded force. Merged PR #251 retains model/body identity, transform, velocities, sleep and wind state in v55. Ordinary Z moved the skull from shelf to floor; F5, separate-process Continue, visible restored floor placement and another F5 pass. Detached preparation does not replace a live save callback. Local and hosted runtime checks pass. Saving while holding an object and matched retail physics remain open. |
| FNV world | Exploration, interiors and some dialogue render in the user's recording. Missing vegetation/models/materials, physics and LOD behavior remain visible in diagnostics. No world/parity completion. |
| Population | Shared Sandbox owns NAVM wandering, activity duration/preferences and ambient conversation attempts. The current candidate completes both reached exterior NPC LooseRaking activities and resumes marker selection or Wander. Doc's bed approach saves. General furniture entry/exit, other activities, voiced exchanges, defensive combat and complete population behavior remain unverified. |
| Animation props | Earlier owned raking checks retained native bones, ANIO rake transforms, idle clocks and sound history across cold reconstruction. Current ordinary exterior raking reaches End and request retirement; F5 retains both marker request graphs. Cold Continue now restores both reached marker actors. Combat interruption and general independent object controllers remain open. |
| Inventory actions | Ordinary hotkey equip/draw and physical Stimpak Drop/Take pass. Repair, Mod, per-instance magazines and loaded-ammo/Take All conservation compile; their added regression cases pass the final gate and broader ordinary use remains unverified. |
| Caravan / VATS | Shared runtime state, input and menus are integrated and compile. A complete ordinary Caravan match remains untested. VATS targeting/cancel and two queued misses produced real shots/ammo/AP effects; hit/damage, cinematic and broader engagement coverage remain open. No retail or physical XR acceptance is claimed. |
| Creature patrol | PR #248 merged source linked routes, retained waits/motion, native walk clips and actor-scoped XPPA results. The Securitron walk's inherited Bethesda rotation layout is decoded. Ordinary play saves/cold-loads patrol, crosses the saved cemetery failure through source-prefix triangle regions, reaches home, changes its quest variable, waits and retires through POEA/POCA. Post-result F5, separate-process Continue without restart and another F5 pass. Native capsule, wall, wrong-floor, saved-facing and local/hosted runtime checks pass. Other patrol inputs, marker idles, duration holds, Sandbox, full Victor dialogue and visual/retail parity remain open. |
| Automatic greeting / actor lifetime | Source editor-position dialogue waits retain native approach state. Ordinary saloon entry, F5 while waiting, cold Continue, walking into Sunny's voiced greeting, Goodbye and another F5 pass. Source corpses skip living AI; detached diagnostics cannot change another owner's corpse capture admission. Full runtime gate and synthetic contracts pass. General Sandbox activity execution remains incomplete. |
| Doc Mitchell tutorial repair | Current source-query and HUD repair passes full healthy source-script execution, five authored messages, cold stage receipts and a native control-label/handoff audit. Ordinary New Game completes the Vigor Tester, questionnaire, skills/traits, final Doc dialogue, exterior traversal and F5 at stage 200. Final release cold Continue, pause/resume and F5 pass; the movement prompt is visually readable after ordinary loading. All optional lessons and retail parity remain open. |
| Flat tutorial | Ordinary saloon Continue, front-door traversal, Sunny's outdoor rifle handoff, R reload, three source tutorial target hits and the next lesson choice reach stage 25. Objective 10 completes; F5 succeeds. Following Sunny and hearing her first-well instruction reaches stage 35 and F5 completes. Placed PGRE/PMIS physical state now restores, allowing that checkpoint to Continue. Invalid linked patrols are skipped; the geckos then reach unimplemented Sandbox. One gecko acquired, pursued and killed the player with melee hits, opening death recovery. Ordinary rifle fire kills both first-well geckos, advances stage 50, transfers Gecko Meat through corpse loot and saves. The next travel marker is elevated above its NAVM floor; projection now permits the downward offset while retaining lateral/upward bounds. Sunny reaches the second well and completes travel; saving after accepting the choice passes. NPC prefetch now retains geometry without binding gameplay until area publication. Ordinary F5 succeeds with two prepared bodies, and again after crossing the cell boundary publishes both actors; their unsupported Sandbox errors remain visible. Separate-process Continue restores both NPC positions and Sunny's completed travel; another F5 and the full local runtime gate pass. PR #254 merged with hosted checks passing. The remaining wells and campfire recipe subsequently completed; Sunny objective 40 and matched retail firing/impact parity remain open. |
| Combat | User rejects combat. Ordinary second-well rifle hits expose a shared enable-parent bug: rendering uses effective enabled state while five combat checks use the local flag, freezing pursuit and hit reactions for quest-enabled actors. The correction now produces ordinary gecko pursuit and repeated melee hits through player death, plus settler/gecko combat. The owned parent-enabled retaliation check and full local/hosted runtime gates pass; PR #255 is merged. Blended-terrain impacts now use a source-position vertex/texture-weight query shared by player and NPC shots. The owned terrain audit and an ordinary aimed LAND shot resolve without material errors; the full local runtime gate passes. Matched retail material results and cell-edge lookup remain unverified. Unique compatible attack-group binding now passes source deathclaw/coyote hit, range-miss and cold-clock fixtures. Ordinary deathclaw terrain encounter, embedded emitters and faction assistance remain open. |
| VR rig | Visible severe rifle hand/forearm deformation around source recording 32:10-32:48. Physical VR acceptance failed. |
| Launch/render | The D3D12 simulator candidate uses a render boundary for launcher entry and preserves stereo through process cleanup. Direct/launcher close checks report zero view-count/type errors and unchanged checkpoints. A separately identified experimental source template also clears the profile RID leak in synthetic/shared-path and owned direct/launcher checks; the official product engine still leaks it. Native API-hash verification and source-template promotion remain pending. Physical D3D12 white output, shader failures, continuous stereo and headset lifecycle acceptance remain open. |
| Saves | Current canonical 02ABC retains completed cooking and north-shoulder traversal, HP 99 and VCG03 objective 40 open. Continue386 restored it; absent historical last-request state later refused further capture. No backfill or overwrite occurred. Current-schema request-history persistence has headless proof; fresh-campaign advancement, off-cell audio, save stalls and physical XR persistence remain open. |
| Quest/scripts | Doc's source-driven opening reaches stage 200 and current-build exterior continuation. Broader campaign scripts, reputation, conversation conditions, triggers and actor-value/effect behavior still require connected gameplay coverage. |
| Audio/UI | Wrist page changes are visible; complete interaction/input release remains unaccepted. Radio broadcast scheduling is unbound. Audio mix/sync is not certified by this review. |
| Traversal | Cross-cell NPC/creature route owners, authored floor destinations and the prior Cheyenne saloon portal remain open. |
| Fast travel | Earlier bounded flat/XR travel worked. Final population and saving after travel remain unaccepted. |
| Performance | Native 3840x2160 travel measured rolling median 21.2512 ms, p95 31.2714 ms and p99 38.2948 ms, failing 4K60. Retained attribution identifies creature Sandbox movement, package pursuit and capsule sweeps; nested totals must not be added. Physical 144 Hz, loading/save stalls and headset acceptance remain open. |
| Builds | Baseline PR #334 passed the full required gate, selected owned audits and all five hosted checks. The experimental package/proof branch has focused fixes; its final required gate remains pending. |
| Content | Official ESM selection logged; installed loose/resource winners still need provenance review. Native extensions disabled; pristine vanilla is not established by plugin selection alone. |
| FO3 | Earlier rendered New Game reached birth stage 22. No accepted standalone FO3 VR release. |
| Mods/TTW | Deferred until standalone games work. |

No campaign completion, complete population/combat/weapon support, retail pixel
parity or physical-headset acceptance is claimed. Original owned files, saves
and the user's recording remain private and untouched.
