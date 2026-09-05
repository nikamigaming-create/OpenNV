# Current work

## Objective and working boundary

Build ordinary New Vegas, Fallout 3 and TTW from the complete winning owned
plugin/resource graph. C# owns formats, gameplay, persistence and telemetry;
Godot owns presentation and input adaptation. Classic Fallout is a later
priority. There is no OpenMW dependency or persistent converted-asset launch path.

Work is on main. The complete repository gate passed on 2026-09-05, including the original
creation controls, dynamic FaceGen, source integer settings, hair base material,
and switchable rendering trace. Both builds, formatting/analyzers, contract
probes, launcher tests and headless Godot loading pass. The recovery checkpoint
preserves these components together; a gate pass is not gameplay acceptance.

The active priority is recovery of the complete starting sequence and Sunny's
tutorial quest, including ordinary input and cold Continue. Reconnect the
existing first-party owners through direct source data; do not restart their
implementation or substitute named success paths. See runtime-recovery.md.
Use recovery-checklist.md as the acceptance queue: 33 requirements remain
open. Component fixes do not close unfinished acceptance requirements.

The immediate user priority is the room: camera, window effects, hue, atmosphere,
focus effects, original fonts and Reflectron UI, all loot and the tutorial skull,
and working pool-table interactions. Original New Vegas character creation is
acceptable; the later Reflectron for classic Fallout is not required here.

## Running capability

- The CLI live harness presents native game buffers and a shared drive console.
  Native D3D9/Godot buffers supply captures. Human priority, leases, stop/release,
  freshness checks and revision waits exist. Bounded previews are not a lossless
  frame trace. Both panels paint received native frames with their source
  sequence and timestamp. Stale buffers receive a visible warning. The prior
  DWM presentation hid whether the displayed buffer advanced. Native display
  cadence remains slower than retail and is still an open defect. Timed key
  releases now resume independently of the WinForms synchronization context;
  actual down/up edges were observed. Semantic tile clicks target their owning
  viewport, including original controls inside rendered devices.
  Recording preserves received native frames, source timestamps and explicit
  transport gaps. Audio and event alignment remain incomplete.
- The private retail adapter supplies held and buffered native DirectInput and
  services capture during Bink. Ordinary Escape skips the intro. Retail has been
  driven through DLC prompts, awakening, naming and Reflections. No OS input,
  Computer Use, quest writes, forced menu-state edits or teleports were used.
- Ordinary native keyboard selection and Enter now complete New/Yes/Bink without
  the stale StartMenu owner. Paired confirmation and movie interruption were
  replayed. The older direct tile-click adapter remains defective; do not use it
  to advance New. A receipt alone is not proof of a state transition.
- OpenNV plays the owned intro directly from Data/Video with in-memory frames
  and sound. Source flags control interruption, pausing and letterboxing.
  Actual New -> movie -> Escape -> Doc CELL has run. Errors do not finish Bink.
- Source triangle winding, wrap/clamp and disabled specular were corrected.
  Engine internal STAT references retain their transforms without editor meshes.
  The red marker around the camera is visibly gone in a native frame.
- The player root now denotes the feet, with capsule/camera offsets on children.
  Native save v7 stores that anchor. Explicit v6 restoration preserves previous
  capsule-centered poses without applying the offset twice.
- NPCs now enter actual assembly instead of counting a skeleton-only model as
  a presented actor. Errors are published per reference; incomplete bodies are
  not published. Doc now assembles through ordinary New with direct skin and
  hair material binding. Source body-part visibility now removes severed caps
  from intact actors. Native pixels still expose bind-pose, head/attachment and
  material defects; assembly is not correct actor presentation.
- Source skeletons, palettes, original weights, strips, inverse binds and Prn
  attachments reach GPU meshes. A native diagnostic exercised 66 skeleton nodes
  and 24 skinned meshes. Its diagnostic material is not a product fallback.
- Direct selected-KF sampling handles compact cubic splines, keyed/constant
  transforms, source visibility and named morph channels. The source skeleton's
  HeadAnims carrier is assembled with its original geometry and relative morphs.
  Complete MTIdle advances actual bones in the native preview audit. Absent native
  visibility targets retain an explicit disposition; missing declared targets
  remain failures. Idle facial-expression propagation and aiming remain unbound.
- The new SayTo owner uses file/PNAM INFO order, supported conditions, Say Once,
  owned voice/LIP resources and audio-completion result commands. The E-key
  shortcut was removed. Synthetic selection and the winning owned voice/LIP
  audit passed. A live New -> movie -> Escape run played four opening INFOs
  and reached player naming (VCG01 stage 10) through audio-completion events.
  Direct TRI expression targets now follow LIP samples on the voice clock.
  Paired run30 records visible mouth motion. Gesture and idle/speech face
  blending, head/eye aiming, spatial voice and matched speech timing remain open.
- Source-discovered dialogue waits replace the product's fixed stage list.
  Entered-stage order and source PlayIdle execution now expose the previously
  skipped mirror handoff. Source-linked ANIO transforms and declared scalar
  extra-data channels bind, and an ordinary replay completes the mirror clip
  and reaches character creation. HeadTrack parameter transport does not supply
  head/eye aiming; that remains unbound. Errors stop progression.
- Start-enabled QUST/SCRI GameMode scripts now execute admitted source branches,
  scalar assignments, ShowMessage and Player.AddItem against winning references.
  Effects commit together; an unsupported reached operation rejects its pending
  effects. The owned audit executes four pack scripts and resolves 19 item stacks.
  Remaining source functions, events and default scheduling are explicit gaps.
  The original MessageMenu reads MESG, XML, prefab art and bitmap fonts. Its
  indexed texture expressions, atlas member paths, installed dark background
  opacity, native glyph baseline and button centering are exercised in paired
  run29. Exact glow, default-button casing and message order/timing remain open.
- Native player inventory is shared by script grants, farewell and persistence.
  Save v8 adds quest stages, Float64 variables, script clocks and pending messages.
  Synthetic rollback, once-only grant and cold restoration checks pass; the owned
  script restoration audit does not duplicate grants or queued messages. Ordinary
  full-opening cold Continue is still required. Legacy saves without script state
  report that missing owner instead of awarding all startup items again.

## Room recovery now exercised

- Source head inverse binds own rigid facial attachments. Hair selects its
  equipped-headgear shape and matching EGM companion. Owned mouth transforms
  disprove an exact-unit-scale restriction; removing it restores the visible
  actor. Native pixels still show incorrect bind-pose/furniture and facing.
- Winning CELL.XCIM resolves IMGS with record-version-specific DNAM layouts.
  The selected old record lacks Skin Dimmer; its cinematic fields now bind in
  their actual positions. The selected audit passes; 16 other image spaces
  expose unresolved flag-layout semantics and remain unsupported.
- NIF alpha blend factors and alpha testing are independent. Window no-lighting
  surfaces use authored cosine falloff and additive blend; source shadows use
  multiplication. Matched window/fog/material evidence is pending.
- Native SLS surfaces sample encoded diffuse data and use source ambient,
  directional/point inputs and fog instead of StandardMaterial3D's linear PBR
  path. This fixes a colour-domain mismatch at the cinematic compositor. Light
  selection, attenuation, shadows, specular and full HDR parameter use remain
  unverified; no exact exposure or final-pixel claim is made.
- Source Add/RemoveScriptPackage, ordered idle lists and begin/change animations
  drive the first-person camera. Camera and ancestor KF channels compose with
  the owned skeleton. New -> movie skip -> dialogue -> naming visibly uses
  wake-up, sit-up and seated clips. Other first-person targets, matched clocks,
  matched camera phase and headset presentation remain unverified. Synthetic hierarchy
  and package tests plus selected owned KF/PACK audits pass.
- The original name menu reads its XML, prefab geometry, DDS/TAI art, FNT/TEX
  glyphs and source labels directly. Its local background replaces the incorrect
  full-screen dark overlay. Native naming and acceptance continue into dialogue.
  Caret, selection/hover styling and exact layout still need matched verification;
  character-creation acceptance remains open. The original device
  now builds directly from its NIF, including the legal binary-alpha storage
  request. Its authored bound yields the same menu-light radius as retail.
  The source XML/FNT menu renders on the original screen with the correct packing:
  a first-party shader-model-2 reader translates the owned pixel program in memory.
  All 16 installed packages pass the selected program audit. A native diagnostic
  shows the source first page in place without fitted screen offsets. The live
  entry now uses this device, original XML/FNT controls and an animated source
  player preview. The installed renderer description selects its shader package.
  A native diagnostic renders all twenty source pages; the compiled declarations
  resolve 31 shape and 12 tone controls into the owned CTL. Source presets, hair,
  eyes, facial hair and RGB edits now have player-state owners. The save selection
  carries face coefficients and head parts. Ordinary opening replay now reaches
  the original Reflectron. Native keyboard audits exercise shape, source colour
  palettes, RGB edits and reopening the saved selection. Integer palette defaults
  resolve from the owned executable with winning GMST overrides. Full cold
  Continue, randomization, portrait manipulation and matched control acceptance
  remain open. No character-creation acceptance is claimed.
- A private live NiCamera/type/frustum observation verifies the world projection
  case: the owned 75-degree FOV uses a 4:3 reference, yielding 59.84044 degrees
  vertically. Godot's 75-degree vertical default was wrong. The general conversion
  and owned near plane now bind; source frustum slopes agree with native matrix
  row magnitudes. Far-plane selection and matched animation phase remain open.
- Source IMAD scripts, normalized curves, lifetime, cinematic channels and fade
  now feed one immutable compositor publication for all passes/eyes. The reader
  admits 297 owned modifiers, including older 236/240-byte DNAM prefixes and legal
  knots beyond playback end. One older layout and one zero-duration animated
  record remain unbound. Selected source audits and synthetic contracts pass.
  Active blur, double vision, radial, depth and other unbound channels remain
  visible in telemetry. Owned opening DOF curves are zero at this phase; blur
  kernels and their native parameter mapping still need binding. Native replay
  of the combined projection/fade change is in progress.
- Image-space publication continues while menus pause simulation. A paired
  replay now shows the committed white IMAD fade behind startup messages; the
  previous publisher skipped that first paused frame. This component fix does
  not establish complete fade/timing or final-pixel parity.

## Actor work in progress

The reusable appearance resolver follows independent template flags, sex, race,
hair, eyes, head parts, inventory, armor slots and BIPL/ARMA addons. The selected
actor has separate head, mouth, teeth, tongue, eyes, facial hair, outfit and
actual glove models. Resource counts and GPU binding are not actor parity.

EGM/EGT readers use documented float scales; the old EGT scale-as-flags decoder
was wrong. Native observation supports NPC plus race geometric coefficients,
source-order accumulation into base vertices, and preserving NIF normals.
The new geometry adapter implements this contract. Remaining floating-point and
current-expression differences remain measured differences.

The live skin material uses an owned FaceMods DDS and native default detail
texture. Both now bind directly. The Hair shader flag admits actor-owned HCLR
RGB, with the HAIR DATA fixed-colour bit preserving the source texture colour.
Synthetic colour-policy checks and the winning Doc appearance audit pass.
The preview's HAIR, EYES, HCLR and three FaceGen coefficient arrays were compared
with a private native observation and matched byte for byte at one checkpoint.
That did not establish rendering agreement: the generic material multiplied
hair vertex RGB into diffuse, suppressing red. Owned lit hair programs instead
compose the authored layer texture and a scalar tint mask. The general hair
base material now implements that equation; ordinary SLS also respects the
source vertex-colour flag. Synthetic equations and selected owned NIF audits
pass. Paired run40 visibly removes the incorrect teal hair. The user accepted
the current hair/age presentation for progressing through the opening. Native
variant selection and anisotropic highlights remain unverified; the actor row
stays open for gesture, attachment and performance gaps.
The selected source furniture pose now places Doc in the seat, but smoking,
entry/exit, facial expression and complete appearance remain unverified.
Direct FRTRI003 delta and stat morphs bind after source NPC/RACE shaping. All 49
owned TRI files pass the selected reader audit. Speech applies actual mesh weights
on the owned LIP clock; run30 shows the mouth moving on nine facial surfaces.
NiBool visibility and named NiMorphData now bind in complete source clips, and the selected PACK idle
collection is published with its missing playback owner. Cigarette scheduling,
idle face channels and their speech blending remain unbound. The earlier full
repository gate now includes the dynamic FaceGen and rendered-menu changes.
Neither closes actor performance.
The intact-body partition fix removes exposed severed caps. No matched material pass exists.
Do not apply the old captured
single tone-map constant to every NPC, substitute generic skin, normalize
source weights, bake a static pose or restore the discarded actor experiment.

## Immediate owners

The active goal is the complete ordinary opening through Sunny's finished
tutorial, with matched retail state, byte/semantic/event comparisons and visible
audio, timing and rendering differences. Stop at each discrepancy, retain both
states and frames, fix its general owner, replay that section, then advance.
Skip the already exercised cinematic when entering an opening comparison.

Transparent, switchable evidence from disk bytes through runtime interpretation
to final pixels supports this loop. The live harness now has trace
ON/OFF, capture, inspection and hash-checked byte comparison commands. Runtime
trace captures winning/observed record byte ranges, retained resource payloads,
NIF block ranges, scene transforms, bones/skin, surface buffers, shader programs,
uniforms, texture readbacks, viewport images and pre/post-draw gameplay state.
Tracing is disabled by default; source read observers detach when disabled.
The inspector links scene nodes to bound resources and source payloads. Its
click selection is explicitly projected-bound candidates: GPU draw execution,
occlusion/alpha contribution, retail frame joins and complete audio events are
still missing evidence. Paired run40 captured a name-menu trace containing 2,477
nodes, 3,046 resources, 627 source resources and 648 records, then another trace
at the first Reflectron page. Turning tracing off detached the observers and
cleared the source event queue while ordinary gameplay advanced. Collection is
expensive and remains opt-in; these captures do not prove complete coverage.

The current matched checkpoint is the first Reflectron page. Highest-priority
remaining presentation defects are the missing item/radio HUD notices, original
menu background blur and opening distortion/focus channels, and black regions
on transparent device/room surfaces. Keep accepted hair work stable while fixing
those owners. The full 33-row acceptance queue remains open.

1. Finish the original startup MessageMenu replay, loading transitions and HUD
   notifications. Confirm shared input, source state, message order and visible
   timing together. Radio station discovery and notifications still lack an owner.
   The full gate passes; matched visual and ordinary progression checks remain open.
2. Finish the source package idle/ANIO and direct lip/aim owners, and restore
   original Reflectron presentation. Doc's floating seated root is visibly
   corrected in private run room-17, but entry/exit and smoking are still open.
   The instance-controller fix passed the full gate and the fan visibly moves.
   The current AI/furniture and menu changes pass the complete repository gate.
   Restore original menu/device presentation from owned XML, atlases and fonts;
   bind remaining IMAD effects and verify matched camera phase. Recover actor
   package/furniture simulation and head aiming for the actual room poses.
3. Continue voice/LIP/INFO playback beyond naming. Bind facial and gesture animation, spatial
   voice, subtitles and remaining script commands to their actual owners.
4. Recover the entire Doc sequence and Sunny's quest through the existing
   general owners, including dialogue, combat, creatures, travel and saves.
5. Expand telemetry at authoritative owners. Polling and lossless transport do
   not observe intervening native events. Event alignment, full draw/material
   state, audio identity and final-frame correspondence remain incomplete.
6. Run selected owned audits and the full repository gate before publication,
   update this handoff with actual results, and publish directly on main.

## Known gaps

The game is not complete. Actor simulation, animation selection/blending,
expressions, broad scripts, dialogue UI, AI/navigation, Havok-equivalent physics,
combat, creatures, exterior streaming, inventory UI, complete saves, FO3 and
TTW ordinary play remain incomplete. The capsule is not a Havok parity claim.
A broad physics audit also exposed an unadmitted UserVersion2=14 NIF layout.

The harness now helps drive and inspect failures; it cannot certify the engine.
State bytes, events, timing, audio, UI, draws and final pixels are independent
comparisons. No complete matched run or whole-game parity has been established.

## Publication commands

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
```

Read architecture.md, status.md, clean-room.md and parity-telemetry.md alongside
this file. Native addresses, captures, decoded owned resources and reverse
engineering dumps stay outside the repository.
