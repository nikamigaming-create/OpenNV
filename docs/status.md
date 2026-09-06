# Current product status

OpenNV is not yet a fully playable implementation of Fallout: New Vegas,
Fallout 3, Tale of Two Wastelands or the classic games. The active goal is the
ordinary NV opening through Sunny's completed tutorial with original
presentation, shared gameplay and persistent saves. All 33 recovery acceptance
requirements remain open. Work stays on main and in this task only; parallel
subagents are stopped at the user's request.

Ordinary New now accepts original creation and the Vigor Tester's complete
SPECIAL allocation, plays Doc's source reaction and psychology introduction,
and reaches VCG01 stage 80. Ordinary room-82 executes Look, source speech and
StopLook, then completes the later furniture package through navigation and
authored entry. The seated endpoint is visually inspected; complete live motion
was not captured. The earlier eight-waypoint couch-route claim incorrectly
used the previous package's retained path. Navigation now identifies its
package/target and actors publish their actual world transforms. Synthetic
continuity and the owned approach/entry/completion audit pass.
See furniture-motion.md.
Player couch activation and the questionnaire remain unbound. Paired original menus show
the same 6/6/6/6/6/5/5 allocation. We are still inside Doc's house; Pip-Boy handoff,
exit and Sunny remain unfinished. The name prompt and Reflectron use
original XML, bitmap controls, dynamic FaceGen, source hair and
voice/LIP. The user accepted the current hair/age appearance for advancing.
Source placement removes the obstructing room wall; clothing retains its own
texture. The gurney wheel report exposed a separate BC1 upload failure after
the earlier falloff correction. Source cutout alpha now survives GPU upload:
all 349,525 owned texels across ten mip levels pass, while the old upload loses
63,464 alpha samples. Ordinary room-79 shows open large-wheel and caster spokes
after original creation, at free movement stage 55. Matched retail shadows,
lighting and pixels remain open. See texture-alpha.md. Original HUD gift
icons, brackets, bitmap font and inventory notices now render. Four Doc
response gestures, chair exit/NAVM travel, IDLE repetition, animation sound,
preview blinking and all eight original Vigor controls pass component audits.
Complete ordinary timing, audio, motion and visual acceptance remain open.

Head tracking now binds optimized float defaults, BPTD, compiled script targets,
slot lifetime and source-bone rotation. Synthetic and native Godot checks,
seven owned opening commands plus the package command, and the full gate pass.
Ordinary room-81 records head publication, animation override and target release.
Dormant introductory references retain an explicit no-process disposition;
missing loaded actors still fail. Automatic/default targeting, eyes, whole-body
mode, process/save restoration and matched pose/frame timing remain unbound.
See head-tracking.md.

Package playback now enforces the actor's source idle replay delay, which was
previously tracked without being checked. Interruption/replacement/expiry
regressions, the selected owned audit and full gate pass. Ordinary room-68
reaches name entry with the seated base and no premature smoking overlay.
Native observation also finds only the seated base active, but phase and camera
alignment remain open. Candidate/parent IDLE conditions now bind after cooldown,
including source NPC faction inheritance and the selected activity/package
predicates. Synthetic, nine owned scenarios and ordinary telemetry pass;
dynamic factions and broader activity owners remain unbound.

Original character confirmation now accepts No/Yes through source scroll-unit
extents, removing false overflow. Vigor opens with its authored sequence and
routes keyboard Up/Down to attribute adjustment. Owned creation and nested
Vigor audits pass. Single-INFO Goodbye speech now plays the original Vigor
audio/LIP, executes its objective update and releases its speech wait. Winning
quest-objective declarations, flags, ordered changes, telemetry and save v11
persistence now bind; synthetic and selected owned checks pass, and ordinary
tester acceptance completes objective 30. General GameMode expression/function
execution now publishes the source SPECIAL calculations before SetStage;
thirty owned SPECIAL/sex cases pass, and the balanced case's six calculated
values match retained native telemetry. Dialogue conditions use authoritative
target sex and quest variables. Ordinary room-77 plays the reaction and the
following three responses. Package event declarations now retain their own
script scope and dispatch once at begin, completion and change. Nine owned
declarations load; empty/comment lifecycles and the reached Look now execute.
Player couch interaction and original questionnaire choices are the next
owners. Post-creation HUD/instructions,
movement/collision differences and Vigor framing remain explicit defects.

Vigor's winning OnActivate program now enforces its stage/objective predicates.
An ordinary early press emits no effects and allows Doc to finish speaking.
The later press opens the original menu and immediately enters its next source
stage. World time and actor animation pause throughout allocation/review, the
original blurred background renders, and acceptance resumes source progression.
Synthetic and six owned cases pass. Source camera framing and the visible
background cabinet remain incorrect.

The whole-face whitening during blinking was caused by zero relative normal
and tangent deltas becoming packed unit directions. Absolute blend-shape
targets now preserve both additive source geometry and the lighting basis.
The original implementation fails the new regression; packed mesh checks, 18
Forward+ samples, ten owned Doc surfaces and the full gate pass. Inspected
ordinary replay frames show blinking without that whitening. Exact native
blink phase, expression normals and complete skin/lighting pixels remain open.

Source quest-clock initialization, shared script lifetime, Float32 recurrence,
modal recurrence and cold consistency pass selected checks. All 252 selected
native initial countdowns match exact bits. Source stage-global writes now
publish the authored night hour. Complete script/block admission, MenuMode,
mutable delays, dynamic start/stop, ForceWeather and aligned event order remain
unbound. The harness uses ordinary keyboard input for retail, rejects internal
UI callbacks and preserves the requested local input lease duration.

Room materials now bind their source vertex colours. CELL direction uses the
native emitted-ray convention, and point lights preserve source radius without
the former 41 percent expansion. All 25 selected night light input triples,
radii and dimmers agree with retained native values. Region diagnostics show
closer floor/wall rendering; these input results do not prove light selection,
shadows, flicker or final pixels.

Lit fog now uses projected vertex distance and interpolation. No-lighting
angular opacity uses the source smooth curve at the vertex stage. Owned source
programs, selected native fog inputs, synthetic contracts and real GPU audits
support these changes. The selected beam region improves from 33.97 to 14.79
colour levels of error, while ceiling error increases from 13.25 to 15.02.
Exact camera/animation/frame alignment remains unestablished.

The trace discovers declared instance parameters and exposed a late attachment
with missing room fog. The cell environment owner now binds new geometry and
cell transfers while isolating preview scenes. GPU lifecycle checks pass, and
ordinary room-64 binds source fog inputs exactly on all 675 declared instances.
Both missing skeleton/ANIO model trace entries are resolved; trace errors and
lost events are zero. See material-fog-and-falloff.md for audit scope.

Reference material emittance now follows winning LIGH/REGN sources, shared sky
time and authored shader flags. Ordinary room-66 matches all 20 sampled native
colour triples and material multipliers. Nine window surfaces formerly used a
fallback path; no-lighting surfaces now share the source shader and retain
managed colour/UV animation. The all-zero source colour rule, instance lifetime
and original Vigor page checks pass. Lit fog still binds exactly on all 675
declared instances. Checked wall error falls from 5.12 to 1.93 and beam error
from 14.79 to 6.83 relative to room-64; ceiling error rises from 15.14 to 15.61.
These are regional diagnostics, not aligned pixel acceptance.

No-lighting fog now applies source vertex distance and destination-factor
composition instead of the default fog treatment. Ordinary room-67 binds fog
inputs exactly on all 743 declared surfaces; all 68 no-lighting selectors
match owned properties, with 20 corroborated by retained native alpha/pass
records. The 20 emittance matches remain intact. The 31-model owned audit,
GPU branch/lifetime checks and full gate pass. Beam error improves modestly
from 6.83 to 6.18, while wall/ceiling errors rise slightly. The three material
audit shutdown leaks are resolved by freeing its local GPU device.

Bounded tracing retains ten image-space GPU surfaces and constants for eleven
passes. Selected native target/cinematic/tint/fade bytes agree with submitted
constants; remaining brightness differences begin before HDR. Native GPU
execution, selected shader/fog toggles, final per-pixel contributions, complete
audio/events and frame correspondence remain incomplete. Opening haze, full
focus/DOF, native per-pass fog admission, premultiplied-program selection and
the world camera far-plane owner still need verification. Private captures
and owned data stay local.

The full Release/Debug, formatting/analyzer, contract, launcher and native
Godot loading gate passed on 2026-09-05. Selected owned material and GPU audits
also pass. Publication and scene acceptance are separate requirements.

The replacement psychology panel and its stage-skip confirmation are removed;
ordinary couch interaction and source dialogue choices remain unbound.
Tag/trait and farewell still contain replacement panels. Complete
Pip-Boy, room loot/skull/pool interactions, radio discovery, exterior
streaming/weather, Sunny's tutorial, broad AI/combat/creatures/physics,
complete saves/plugins, ordinary FO3/TTW and integrated OpenXR remain unfinished.
No whole-scene, opening, tutorial or campaign parity claim has been established.
See current-work.md for current owners and scene-defects.md for discrepancies.
