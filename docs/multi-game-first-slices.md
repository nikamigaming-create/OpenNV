# Multi-game launcher and first-slice delivery

Status: **the compact launcher always exposes Fallout 1, Fallout 2, New Vegas,
and Fallout 3 with one visible FPS/Hex/VR selector. Current bounded admissions
are Fallout 1 Hex/FPS, Fallout 2 Hex, and New Vegas FPS plus experimental
OpenXR; other modes stay visible and disabled. TTW-FO3 and TTW-FNV are disabled
editions under the matching game cards, and JAM remains gated**.

This plan coordinates the common launcher/boot surface across Fallout 1,
Fallout 2, Fallout: New Vegas, Fallout 3, TTW, and JAM. It does not replace the canonical
New Vegas [whole-game delivery plan](whole-game-delivery-plan.md) or any
game-specific evidence contract.

## Product boot contract

Every profile advances through explicit persisted states:

```text
registered inputs -> verified cache -> supported startup legal/logo movies -> main menu
main menu -> New / Continue / Load -> narrative movie/opening
          -> first playable slice -> cold save/reload
```

That is the intended common contract, not the status of every route today. New
Vegas currently owns the menu-to-New-Game-to-intro-to-Doc path. The registered
Fallout 1 route begins at a functional OpenNV original-style menu, opens the
owned character picker through New Game, continues through the owned Overseer
movie, and enters V13ENT. A valid cold save adds Continue; that route bypasses
the picker/movie/entry reset and restores the saved hex, player presentation,
Pip-Boy/classic HUD, and exact finite camera mode/yaw/pitch/zoom under black
before controls. Saves without the required player-identity and camera contracts
remain non-continuable rather than receiving guessed migration. The menu is an asset-free adaptation; the complete
retail startup-logo and original-menu presentation remain absent.

- Escape and a visible Skip action converge on the same post-movie state.
- Skipping presentation never skips required gameplay or character-state work.
- Menu actions use the same campaign/save owners as direct acceptance routes.
- Flat, FPS/hex, and OpenXR are presentation/input adapters over one profile
  state; changing presentation does not fork inventory or quest truth.
- Fallout 1/2 use owned FRM/MAP/PRO data. Fallout 3/New Vegas Hex modes use the
  owned NIF/DDS/ESM cell graph directly with a reimagined tactical hex adapter;
  they do not require fabricated FRM files.
- Retail and mod inputs are read-only. Generated caches are local, disposable,
  hash-verified, and never distributed.

## Ordered recovery and delivery

### P0 — Recover and join existing routes

1. Available now: `.\scripts\Start-OpenNV.ps1` connects the source launcher to
   the checked-in Godot runtime.
2. Available now: **Register Fallout 1 cache** selects and validates generated
   `hex-scene.json` and `character-start.json`, then passes their paths, the
   character-start hash, the selected presentation, and an isolated save path
   to Godot.
3. Available now: Fallout 1 Hex Tactical and First Person are two views of one
   Vault Dweller state. The V13ENT OpenXR adapter reaches that same state and
   has simulator proof for tracking, locomotion, snap turn, fire, reload, and
   save, but XR door use, campaign-native hands/weapon/UI, and physical-headset
   acceptance remain open. Fallout 1 OpenXR therefore stays disabled. The
   selected premade/custom identity, sex, presentation authority, and
   weapon-visual suppression policy persist through cold restore into the
   gameplay actor. Continue also restores the authoritative saved player hex and
   Hex/FPS/shoulder camera mode, yaw, pitch, and zoom before releasing controls.
   Incomplete older saves are not migrated and do not expose Continue. GCD/FRM
   remains authoritative. Every classic 3D humanoid requires the shared owned
   FNV donor preview set with both sex variants and verified body/outfit/socket
   hashes; there is no procedural, FRM-player, silhouette, or standee fallback.
   Classic scripts require an explicit `-ClassicHumanoidInstallManifest`; their
   resolver verifies the install output, its opening-manifest join, and the
   standalone preview-set hash before runtime without cache discovery.
   The donor is presentation-only and non-parity. Fresh producer/native visual
   acceptance is still open.
4. Available now: the normal New Vegas menu and skippable intro enter the Doc
   opening, and the accepted checkpoint/reload path completes character setup,
   farewell, and the stage-200 open-world-ready save. The default bounded
   composite eagerly instantiates the Doc house, Goodsprings exterior active set, saloon,
   LAND, actors, and both reciprocal XTEL pairs with shared gameplay/save state.
   A source-bound HUD/STATS/ITEMS/DATA and Pip-Boy shell
   now consumes that same state; STATS explicitly reuses the verified ITEMS
   frame until its remaining layout expressions execute. From a completed
   stage-200 Continue, configured flat input traverses both forward XTEL links;
   campaign save v6 persists source-derived Level/HP/AP/XP plus saloon CELL
   `00106185`, and a fresh owned-menu Continue restores the unchanged save and
   player transform there. Only the authoritative current CELL renders,
   processes, collides, emits lights, and advances actor grounding; linked CELLs
   remain hidden, frozen preload state. The current pair uses the admitted
   four-family cache, binds activation to the exact selected source door, and
   passes first-run plus cold-Continue validation. Complete tile
   interaction, reverse traversal, neighboring exterior-grid streaming,
   integrated-route OpenXR acceptance, and retail visual parity remain open.
   The character flow binds the prepared owned `TextEditMenu` and `RaceSexMenu`
   backgrounds. The retained bounded native gate composes chair FURN `001059b0`
   from the hash-bound marker-14 NIF/GMST contract. Current code also binds the
   patient bed fail-closed to `REFR 00103e5b`, base FURN `00106a6a` /
   `NVbedtwin01`, and the owned model hash; the retained run predates that exact
   bed-identity correction, so it needs a fresh native/retail differential.
   Owned cigarette `ANIO 00083519` attaches to `Bip01 R Hand`. Its admitted NIF
   and smoking KF provide no particle/effect-spawn contract, so visible puffs are
   explicitly a first-party, tip-anchored non-parity adaptation. Stage 36
   accepts visible name input and all 43 admitted CTL/EGM controls on the owned
   default-male head-only FaceGen preview. Female and other non-default
   identities lack live 3D face rendering, and the preview is not a reusable
   full-body player actor. The run transfers the exact stage-40 exit root and
   reaches the stage-55 autosave. It did not cold-resume or create accepted
   media. Its report is
   `D:\Builds\OpenNV-fnv-headless-exact-creator-20260829-r1\checkpoint-marker-v3-scoped2-report.json`,
   SHA-256
   `894598ee8644cb2ac3869fd645c420882f9980c722d96277d46f1d09f35e645b`.
   An uninterrupted whole-campaign route is not proven.
5. Available now: `scripts/Register-OpenNVFallout2.ps1` validates the legally
   owned `master.dat`, `critter.dat`, and `patch000.dat` DAT2 archives and emits
   a hash-bound source-only profile. The launcher shows Fallout 2 as the fourth
   game and enables Hex only after its five matching local slice artifacts are
   present; FPS and VR remain disabled.
6. Available now: `content/tools/fo2_first_slice.py` resolves effective Map 126
   through patch/critter/master overlay precedence and emits its exact MAP
   header and elevation, MAP-header player entry marker, scripts, 567 placed
   objects (568 including inventory), 37 PRO identities, and 34 required FRM
   identities as an asset-free local manifest. It does not establish the
   executable-owned new-game selection policy or implement a runtime.
7. Available now: `content/tools/prepare_fo2_temple_presentation.py` consumes
   that exact graph and decodes only its floor/roof tile frame zero and exact
   placed-object frame/rotation pairs using the owned palette. The generated
   PNG cache is disposable, local-only, hash-bound, and non-distributable; no
   runtime readiness is implied.
8. Available now: the Fallout 2 Temple runtime contract verifies the complete
   cache/source/profile/recipe chain and constructs all admitted non-default
   floor patches and top-level object FRM planes in Godot's 3D hex space. It
   derives exact floor support, a central-hex blocker walk mask, and connected
   wall-shell collision from owned MAP fields; headless rays prove the floor and
   wall colliders. A source-bound nonvisual cursor proves adjacent movement and
   boundary rejection inside the exact entry component. Multihex footprints,
   General Temple scripts, target AI, broad interaction/gameplay, FPS, and
   OpenXR remain unresolved. One later strict source-bound Villager/Spear
   adapter now supplies player-controlled AP melee, defeat/loot, visible state,
   and save/cold restore without claiming those broader systems.
9. Available now: an asset-free transition compiler binds all 18 Map 126 exit
   grids, the zero door-prototype count, `ARTemple.int`, and three live MAP
   script records. The ordinary runtime follows the exact source walk path from
   Map 3 to exit serial 1738 and loads owned ARTEMPLE Map 126 at tile 16486,
   elevation 0, rotation 0. It does not execute INT bytecode.
10. Available now: a separate Map 3 `ARCAVES` compiler proves that incoming
    tile/elevation/rotation, 24 reciprocal exits to Map 126, 18 reachable exits
    in the 586-hex arrival component, 298 owned resource identities, and all 173
    disposable presentation artifacts. Godot renders that cache and grounds an
    input-driven source-walk-gated arrival body at exact tile 28707; the admitted
    Map 3-to-126 exit now executes and cold-restores, while remaining reciprocal
    runtime exits remain absent.
11. Available now: an exact 432-byte GCD compiler plus disposable local cache
    binds the owned Narg, Mingan, and Chitsa profiles, BIO text, picker/panels,
    and PRO/FID-linked male/female AA idle plus 6-direction, 8-frame AB walk
    FRMs. The visible Godot selector supports keyboard and
    mouse choice. Modify/Create edit name, sex, age 16–35, and seven 1–10
    SPECIAL values totaling 40. Modify preserves source tags/traits and Create
    leaves them unselected. Confirm applies the sex-correct FRM before Map 3.
    Each distinct owned premade panel remains available; its Live 3D toggle and
    the Map 3 gameplay actor require the same owned FNV full-body donor preview
    set. It must provide the selected sex and verified body/outfit/socket join;
    `-ClassicHumanoidInstallManifest` verifies the install/opening output join
    and standalone preview-set hash before Godot; absent or incompatible input fails closed, with no procedural
    or FRM-player substitute. GCD/FRM identity stays authoritative and the
    donor is non-parity presentation. Source MAP/PRO/FRM remains the environment
    authority. The version-12 atomic save preserves character mode, source basis,
    explicit appearance/portrait state, custom
    profile, map/elevation/tile/facing, transform, bounded modes, and the exact
    source exit identity. Ordinary grounded movement follows the 13-step path
    from Map 3 tile 28707 through exit serial 1738 into ARTEMPLE Map 126 tile
    16486; fresh
    male and female processes prove two directions, return to owned AA idle,
    and cold-restore the same state. Tag/trait editing, other animations,
    campaign-wide state, remaining exits, and campaign play remain absent. One
    exact MAP/PRO/MSG-bound Villager supports bounded player HP/AP melee and
    defeat-to-nested-Spear loot with cold restore. An alternative exact
    tagged-Speech Cameron branch keeps Klint alive, reaches live ARVILLAG, applies
    one configured input from tile 11683 to 11482, and cold-restores there. The
    branches do not merge or imply a dead-guardian village shortcut; their hashes
    and state boundaries are recorded in the [canonical FO2 branch ledger](evidence/fo2-first-slice-branch-ledger.md).
    Target AI/turns, general INT execution,
    general combat/inventory, classic-body visual parity, custom face/hair/skin
    editing, generated custom portraits, and retail parity remain absent. The
    non-source opaque Temple wall proxy is removed while source-derived collision
    and all 45 owned wall FRMs remain; classic fixed-Y composition is non-parity.
    The Elder movie's natural end and Skip converge on the exact terminal source
    frame/fade, black handoff, same live camera, reveal, and control release; the
    live presentation remains human-review/unaccepted. The 22 admitted torch
    anchors use exact owned opaque FRM emitter pixels/centroid and source MAP
    light placement. That emitter is static; flame animation and smoke are not
    transported.

Current result: a normal launcher starts either registered Fallout 1 view, the
registered and prepared Fallout 2 Hex character-to-Arroyo slice, or the bounded
New Vegas menu/Doc/exterior/saloon composite. Fallout 3 remains launcher-disabled
with a registered development menu/CG00 frontend but no playable presentation.

### P1 — Fallout 3 first slice

1. Available now: `.\scripts\Register-OpenNVFallout3.ps1 -Fallout3Root <install>`
   registers and hashes a legal GOTY installation as a distinct read-only local
   profile.
2. Available now: that profile resolves the six-plugin GOTY stack, required
   archives, five menu XML inputs, menu art/music, the intro and four age
   transition movies, CG00-CG04, and the sex/name/appearance selection inputs.
3. Available now: the profile-backed main menu plays the locally transcoded,
   hash-verified owned intro. Escape and its visible Skip control converge on
   the same CG00 sex/name route, which persists its stage-60 character state.
   Continue reopens that exact profile-bound state. The appearance route uses
   the owned playable RACE records, sex-aware HAIR/EYES lists, and composed
   Player-plus-RACE FaceGen defaults; acceptance persists stage 62. Its preview
   is the exact owned head/hair/eye source textures, not a 3D face render. The
   compiler also resolves the birth CELL,
   player marker, Doctor Li reference/base, 1,610 references, 401 bases, and
   exact model/texture inputs.
4. Available now: the source-bound `CG00PlayerSection4` package, exact stage-65
   parent race/FaceGen commands, stage-80 package/variable/reference commands,
   zero-command stage-85 result, and four-command stage-90 INFO `0001f379`
   compile and validate fail-closed. The ordinary bounded route enters the owned
   birth room, plays the Dad cue, applies those results plus the owned stage-90
   white fade/sound, executes seven of eight exact stage-100 commands through
   `SetPCYoung 1`, and cold-restores stage 100 without replaying one-shot
   effects. A fresh profile now compiles both opening movies without decode
   errors plus the exact CG01 stage-0/stage-5 tree and sex-specific two-line
   Dad-speech transition into stage 10. It now also compiles objective 10
   (`Walk to Dad.`), the unique source `SCPT`→`ACTI`→`REFR 0002ea54` trigger,
   and the exact four-command stage-12 result. A native acceptance pass applies
   that trigger state and cold-restores stage 12 at
   `D:\Builds\OpenNV-fo3-cg01-stage12-20260829-r1`. The profile SHA-256 is
   `a77692f69cf958d769f93b96dc62554b48e5b12d34e8850a8fc14f91181f3704`;
   apply/restore report SHA-256 values are
   `5b440b505a9d02549b976e49bd3c49392fc712f4b5b1b5aeb3195b18941be98c`
   and `3996e216be77b57200cb5201a216ff04ce39e3d0dd4f5e75a67894870f4df614`.
   This is command/movie-surface/trigger/save evidence, not a playable toddler
   world: normal world-space locomotion and physical trigger entry still stop
   at stage 10.
   The current early-birth runtime additionally starts admitted participants
   from exact source `PACK` sections, selects hash-bound KF sequences, and
   composes the sampled `Camera1st` skeleton node through its animated parent
   chain without a guessed `NiCamera` axis flip. The current toddler acceptance
   path still auto-steers; ordinary configured user input, physical trigger
   entry, exact actor/camera timing, and a matched retail/native differential
   remain gates. These implementation fixes do not promote the route.
   Compile the eighth stage-100 command and the remaining
   authored birth/age/SPECIAL/tag/trait sequence,
   dialogue, packages, controls, Vault 101 cells, actors, scripts, inventory,
   collision, NAVM, doors, and save boundary into neutral versioned contracts.
5. Prove the ordinary menu route through character creation and the first
   exterior handoff, then cold-reload the exact state in flat mode.
6. Add OpenXR only through the shared gameplay state and run physical headset
   acceptance separately.

Exit: a launcher-created standalone Fallout 3 character completes the bounded
Vault 101 opening and persists outside. This is not a full-campaign claim.

### P2 — TTW combined-world profile

1. Require player-generated TTW output from legally owned Fallout 3/New Vegas
   inputs; never download, bundle, or regenerate TTW inside a release.
2. Available now: `content/tools/ttw_profile.py` registers the ordered read-only
   data roots and active load order, validates TTW markers and plugin master
   closure, hashes active plugins, inventories effective BSA names, and emits a
   distinct save-compatibility identity.
3. Available now: the launcher exposes TTW-FO3 under Fallout 3 and TTW-FNV under
   New Vegas. The isolated TTW-FO3 executor applies and cold-restores the exact
   source stages `CG00:0/60/100` and `CG01:0/5`, including the synchronously
   nested stage 5, with 38 commands and identical state SHA-256
   `a4d3b74e5d7e4a83c409138e377aa17ac54d7387b6a23f2c5e6e5db1c7d53e58`.
   It preserves dedicated `ttw:` save and `ttw-fo3-opening:` cache identities.
   The current FO3 profile producer deliberately consumes the records-only
   effective-source entry point. The archive/loose-member resolver is not wired
   to a playable world, and TTW-FNV has no effective-stack Doc profile/runtime;
   both launcher editions remain disabled.
4. Connect the existing archive-member/loose-file resolver and the remaining
   Vault 101 cell resources, reference transforms/world
   application, owned movie transcode/playback, CG01 stage 10 and later
   gameplay, and first-party xNVSE/JAM semantics.
5. Prove TTW's selected start, character sequence, first playable slice,
   persistence, and later the authored inter-wasteland transition.

Exit: TTW is a separate launcher path with new-character enforcement. It never
adopts a standalone Fallout 3 or New Vegas save.

### P3 — JAM semantic compatibility

1. Available now: register and hash-bind a user-installed JAM profile and its
   declared prerequisites with `content/tools/jam_profile.py`.
2. Available now: bounded first-party transport covers JVS sprint and JBT
   time-dilation settings/semantics only. This is not an xNVSE implementation.
3. Unsupported: native DLL loading and portable xNVSE, JIP LN, JohnnyGuitar,
   kNVSE, Stewie Tweaks, UIO, plus the remaining JAM script/event/UI/AP/
   animation/audio/cosave surface.
4. Transport portable content normally and implement only the required native
   semantics as first-party Godot capabilities. Never load arbitrary DLLs.
5. Gate each JAM module independently, then gate the complete selected set in
   New Vegas and separately in TTW, including save removal/retention policy.

Exit: the launcher enables JAM only when its complete required capability set
is supported for the selected base profile.

## Current blockers

| Route | Blocking owner |
| --- | --- |
| Fallout 1 Hex/FPS | Registered V13ENT route works. Continue fail-closed restores the saved hex, complete player identity/presentation policy, Pip-Boy/classic HUD, and finite Hex/FPS/shoulder camera state without replay or entry reset. Older saves missing identity or camera state remain non-continuable. All 3D humanoids require the shared owned FNV donor preview set; missing input has no substitute body. A fresh producer/native visual acceptance is still required, and the rest of the campaign is not connected. |
| Fallout 1 OpenXR | Shared-state V13ENT adapter passes simulator movement, turn, fire, reload, and save; XR door use, campaign-native hands/weapon/UI, launcher enablement, and physical-headset acceptance remain |
| Fallout 2 | The launcher enables the bounded Hex route when all local identities match and a shared owned FNV donor preview set validates. The creator/player 3D body has no procedural or FRM-player fallback; GCD/FRM remains identity/gameplay authority and MAP/PRO/FRM remains environment authority. The retained checkpoints are an alive-Klint peaceful Cameron route through live ARVILLAG input/save and an alternative Temple guardian AP-combat/Spear-loot/equip/save route. They intentionally do not merge; see the [canonical branch ledger](evidence/fo2-first-slice-branch-ledger.md). Elder normal-end/Skip state convergence and exact static source-FRM torch pixels/MAP lights are implemented, but the live reveal remains visually unaccepted and flame animation/smoke are not transported. Full campaign, FPS, VR, target AI/turns, general INT execution, and broad gameplay remain absent. |
| New Vegas first slice | Exact source inputs now bind chair `001059b0`, patient bed `00103e5b` → `00106a6a`, and right-hand cigarette ANIO `00083519`. The bed correction still needs a fresh native/retail differential. The admitted cigarette NIF/KF has no particle/effect contract, so visible smoke remains a first-party tip-anchored non-parity adaptation. Complete actor/package execution, exact phase/pixels, integrated OpenXR acceptance, and uninterrupted campaign continuity remain open. |
| Fallout 3 | Source `PACK`-selected KF publication and `Camera1st` parent-chain composition are implemented without a guessed `NiCamera` axis flip. The current toddler proof auto-steers, so ordinary configured input, physical trigger entry, exact actor/camera timing, and matched retail/native evidence still gate every Fallout 3 launcher mode. |
| TTW | The launcher splits TTW-FO3 and TTW-FNV editions. The bounded TTW-FO3 compiler/executor consumes effective records only and cold-restores its isolated command state; resource winners are not connected to a world. TTW-FNV has no effective-stack Doc profile/runtime. Both remain disabled. |
| JAM / xNVSE | Bounded JVS sprint and JBT time-dilation transport exists. Native DLL loading and portable xNVSE/JIP/JohnnyGuitar/kNVSE/Stewie/UIO/JAM script/event/UI/AP/animation/audio/cosave semantics are unsupported, so JAM remains disabled. |

The runtime manifest is the executable truth. Documentation may describe this
sequence, but a route stays disabled until its direct gate passes.
