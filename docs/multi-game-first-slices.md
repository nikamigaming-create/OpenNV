# Multi-game launcher and first-slice delivery

Status: **the compact four-game launcher exposes bounded Fallout 1 Hex/FPS,
Fallout 2 premade-or-custom-to-Arroyo Hex, New Vegas opening/Goodsprings, and a
Fallout 3 CG00 development frontend; TTW has an isolated command/save executor
but is not runtime-ready, and JAM remains dependency-gated**.

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
movie, and enters V13ENT. The menu is an asset-free adaptation; the complete
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
   acceptance remain open. Fallout 1 OpenXR therefore stays disabled.
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
   backgrounds. The current stage-10 frame is
   `D:\Builds\OpenNV-fnv-doc-opening-20260829-r3-owned-menu-nav\stage10-owned-menu-nav.png`,
   SHA-256
   `5096233bbcf0293191c83dd4fbdaf0ce5f5d3aed16ba380eb31f1c3d7e744c28`.
   Doc no longer receives the raw chair-reference elevation. The native
   `D:\Builds\OpenNV-fnv-furniture-occupancy-proof-20260829-r1` proof preserves
   his authored ACHR transform on FURN `001059b0` and releases it for stage 40;
   exact seated-loop, entry, and exit visuals remain unsupported. The native
   `D:\Builds\OpenNV-fnv-cigarette-proof-20260829-r2` proof shows source
   `ANIO 00083519` default-hidden, visible for `IDLE 00071ee3`, and hidden on
   idle exit. No source-backed smoke emitter was found, so smoke is absent. An
   uninterrupted whole-campaign route is not proven.
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
    The version-4 atomic save preserves character mode, source basis, custom
    profile, map/elevation/tile/facing, transform, bounded modes, and the exact
    source exit identity. Ordinary grounded movement follows the 13-step path
    from Map 3 tile 28707 through exit serial 1738 into ARTEMPLE Map 126 tile
    16486; fresh
    male and female processes prove two directions, return to owned AA idle,
    and cold-restore the same state. Tag/trait editing, other animations,
    campaign-wide state, remaining exits, and campaign play remain absent. One
    exact MAP/PRO/MSG-bound Villager supports bounded player HP/AP melee and
    defeat-to-nested-Spear loot with cold restore; target AI/turns, INT/dialogue,
    general combat/inventory, and retail parity remain absent. The
    non-source opaque Temple wall proxy is removed while source-derived collision
    and all 45 owned wall FRMs remain; classic fixed-Y composition is non-parity.

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
   Dad-speech transition into stage 10. A portable native proof now applies and
   cold-restores the stage-10 runtime/save state. The private proof root is
   `C:\Users\nbrys\AppData\Local\OpenNV\private-proof\fo3-cg01-stage10-portable-20260829-r1`;
   apply/restore report SHA-256 values are
   `ff3c3957c1e2936ec470eb0f578f5e3f12680bc95b4993051b7984239f10fc87`
   and `257512e45a92f5d2b184795c3082258526031818e00d0c18a8d034e86ec51f93`
   for profile SHA-256
   `4233253dfc347694ab7e4cbc8ee76961ee21e0cfb65db753be95fdf997d64833`.
   This is command/movie-surface/save evidence, not a playable toddler world.
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
3. Available now: the isolated TTW executor applies and cold-restores the exact
   source stages `CG00:0/60/100` and `CG01:0/5`, including the synchronously
   nested stage 5, with 38 commands and identical state SHA-256
   `a4d3b74e5d7e4a83c409138e377aa17ac54d7387b6a23f2c5e6e5db1c7d53e58`.
   It preserves dedicated `ttw:` save and `ttw-fo3-opening:` cache identities;
   `runtimeReady` remains false.
4. Compile general archive-member/loose-file override precedence and connect
   the remaining Vault 101 cell resources, reference transforms/world
   application, owned movie transcode/playback, CG01 stage 10 and later
   gameplay, and first-party xNVSE/JAM semantics.
5. Prove TTW's selected start, character sequence, first playable slice,
   persistence, and later the authored inter-wasteland transition.

Exit: TTW is a separate launcher path with new-character enforcement. It never
adopts a standalone Fallout 3 or New Vegas save.

### P3 — JAM semantic compatibility

1. Available now: register and hash-bind a user-installed JAM profile and its
   declared prerequisites with `content/tools/jam_profile.py`.
2. Inventory its exact records/assets plus every xNVSE, JIP LN,
   JohnnyGuitar, kNVSE, UIO, and lStewieAl command/event/UI dependency.
3. Transport portable content normally and implement only the required native
   semantics as first-party Godot capabilities. Never load arbitrary DLLs.
4. Gate each JAM module independently, then gate the complete selected set in
   New Vegas and separately in TTW, including save removal/retention policy.

Exit: the launcher enables JAM only when its complete required capability set
is supported for the selected base profile.

## Current blockers

| Route | Blocking owner |
| --- | --- |
| Fallout 1 Hex/FPS | Registered cache route works; only V13ENT is playable and the rest of the campaign is not connected |
| Fallout 1 OpenXR | Shared-state V13ENT adapter passes simulator movement, turn, fire, reload, and save; XR door use, campaign-native hands/weapon/UI, launcher enablement, and physical-headset acceptance remain |
| Fallout 2 | The launcher enables the bounded Hex premade-or-custom route when all owned-profile/cache identities match; exact Map 3 exit serial 1738 loads ARTEMPLE Map 126 and cold-restores. One strict source-bound Villager/Spear adapter provides visible player HP/AP, deterministic adjacent melee, defeat/loot inventory, and v4 cold restore. Target AI/turns, INT/dialogue, general actors/combat/inventory, tag/trait editing, campaign-wide persistence, remaining exits, full campaign, FPS, VR, and parity remain absent |
| New Vegas first slice | Menu/intro/Doc house, source-ordered Doc speech/quest beats, source-derived Level/HP/AP/XP, the source-bound HUD/Pip-Boy runtime shell, and the bounded eagerly instantiated Doc/exterior/saloon composite load; the current configured-input route and cold Continue pass against the admitted four-family cache. Current-CELL-only render/collision activation prevents linked interior/exterior shells from presenting together, and one-time source-collision grounding reports actor corrections. The owned TextEdit/RaceSex backgrounds are bound; the current stage-10 frame is `D:\Builds\OpenNV-fnv-doc-opening-20260829-r3-owned-menu-nav\stage10-owned-menu-nav.png`, SHA-256 `5096233bbcf0293191c83dd4fbdaf0ce5f5d3aed16ba380eb31f1c3d7e744c28`. Doc no longer receives raw chair elevation. A native furniture proof preserves his authored ACHR/FURN occupancy and releases it at stage 40, but exact seated-loop/entry/exit visuals remain unsupported. A second native proof binds cigarette `ANIO 00083519` to `IDLE 00071ee3` with default-hidden and exit-hide behavior; no source-backed smoke emitter was found or implemented. The [source package audit](evidence/fnv-goodsprings-actor-package-contract.md) pins Doc, Pete, Trudy, settler, Sunny, and Cheyenne schedules and conditions, but those generic package/quest executors are not implemented. The exact startup player-root/camera, complete population/package AI, exterior surface/directional lighting, dynamic time/weather, reverse traversal, integrated OpenXR acceptance, complete tile interaction, retail UI parity, uninterrupted campaign continuity, neighboring-world streaming, and visual parity remain |
| Fallout 3 | Owned-profile menu/intro/Escape convergence and persistent CG00 sex/name/appearance enter the bounded Vault 101 birth room and reach/cold-restore stage 100 through seven of eight exact stage-100 commands. A fresh pinned-Theora profile compiles the CG01 stage-0/stage-5 and Dad-speech → stage-10 contracts; the portable native stage-10 apply and cold-restore reports pass. FPS/Hex/VR, the post-stage-10 toddler world interaction, general package/dialogue/KF AI, the eighth stage-100 command, and freely playable Vault 101 remain |
| TTW | The strict profile/effective-source namespace and bounded FO3 CG00→CG01-stage-5 command/movie contract compile and validate against the installed stack; the isolated executor applies and cold-restores 38 exact source commands under dedicated TTW cache/save identities. `runtimeReady` remains false because Vault 101 cell-resource compilation, reference-transform/world application, owned-movie transcode/playback, CG01 stage 10 and later gameplay, and xNVSE/JAM native-plugin semantics remain absent |
| JAM | Dependency registrar plus bounded JVS sprint and JBT time-dilation semantics work; missing dependencies and portable xNVSE/JIP/JohnnyGuitar/kNVSE/Stewie/UIO/JAM semantics keep the launcher toggle disabled |

The runtime manifest is the executable truth. Documentation may describe this
sequence, but a route stays disabled until its direct gate passes.
