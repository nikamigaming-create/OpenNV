# Multi-game launcher and first-slice delivery

Status: **the compact four-game launcher exposes bounded Fallout 1 Hex/FPS,
New Vegas opening/Goodsprings, a launcher-disabled Fallout 2 owned-premade-to-
Arroyo development route, and Fallout 3 CG00; TTW runtime is absent and JAM
remains dependency-gated**.

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
   composite preloads the Doc house, Goodsprings exterior active set, saloon,
   LAND, actors, and both reciprocal XTEL pairs with shared gameplay/save state.
   A source-bound HUD/STATS/ITEMS/DATA and Pip-Boy shell
   now consumes that same state; STATS explicitly reuses the verified ITEMS
   frame until its remaining layout expressions execute. From a completed
   stage-200 Continue, configured flat input traverses both forward XTEL links;
   campaign save v5 persists saloon CELL `00106185`, and a fresh owned-menu
   Continue restores the unchanged save and player transform there. Complete
   tile interaction, reverse traversal, neighboring CELL streaming,
   integrated-route OpenXR acceptance, and retail visual parity remain open.
   An uninterrupted whole-campaign route is not proven.
5. Available now: `scripts/Register-OpenNVFallout2.ps1` validates the legally
   owned `master.dat`, `critter.dat`, and `patch000.dat` DAT2 archives and emits
   a hash-bound source-only profile. The launcher shows Fallout 2 as the fourth
   game and keeps Hex/FPS/VR disabled.
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
   Temple player controls, scripts, interaction, gameplay, save, FPS, and
   OpenXR remain unresolved.
9. Available now: an asset-free transition compiler binds all 18 Map 126 exit
   grids, the zero door-prototype count, `ARTemple.int`, and three live MAP
   script records. The headless runtime moves to a reachable source exit and
   applies only its owned Map 3 / tile 28707 destination state. It does not load
   that destination or execute INT bytecode.
10. Available now: a separate Map 3 `ARCAVES` compiler proves that incoming
    tile/elevation/rotation, 24 reciprocal exits to Map 126, 18 reachable exits
    in the 586-hex arrival component, 298 owned resource identities, and all 173
    disposable presentation artifacts. Godot renders that cache and grounds an
    input-driven source-walk-gated arrival body at exact tile 28707; reciprocal
    runtime execution remains absent.
11. Available now: an exact 432-byte GCD compiler plus disposable local cache
    binds the owned Narg, Mingan, and Chitsa profiles, BIO text, picker/panels,
    and male/female idle FRMs. The visible Godot selector supports keyboard and
    mouse choice, then Take applies the selected source state and sex-correct
    FRM before the Map 3 handoff. Modify/Create, editable fields, persistence,
    animation playback, and campaign play remain absent.

Current result: a normal launcher starts either registered Fallout 1 view
through its menu/creator/movie path and the bounded New Vegas menu/Doc/exterior/
saloon composite. Fallout 2 and Fallout 3 remain launcher-disabled;
Fallout 3 has a registered development menu/CG00 frontend but no playable
presentation, while Fallout 2's exact source-bound Godot scene can be
rendered through a visible selectable premade-to-Arroyo development route
without persistent gameplay or launcher promotion.

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
   and zero-command stage-85 result compile and validate fail-closed. The normal
   UI does not apply or persist them because the authored package/dialogue
   triggers and Vault 101 world are absent. Compile the authored
   birth/age/SPECIAL/tag/trait sequence,
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
3. Compile archive members, loose-file precedence, records, scripts, and the
   same command-capability inventory over the effective
   TTW stack and reject every ambiguous or unsupported winner.
4. Prove TTW's selected start, character sequence, first playable slice,
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
| Fallout 2 | Exact Temple/Arroyo Caves source transport and a rendered Map 3 arrival now include an owned Narg/Mingan/Chitsa selector, source stats/bios/portraits, sex-correct HMWARR/HFPRIM art, Take handoff to tile 28707, and grounded source-walk-gated movement. Modify/Create, editable fields, persistence, reciprocal runtime exit execution, INT, actors, combat, inventory, full campaign, FPS, VR, parity, and launcher promotion remain absent, so all modes stay disabled |
| New Vegas first slice | Menu/intro/Doc house, the source-bound HUD/Pip-Boy runtime shell, and the bounded preloaded Doc/exterior/saloon composite load; diagnostic portal checks pass, and completed-save owned Continue drives configured flat input through both forward XTEL links before v4 cold-restores saloon CELL `00106185`; reverse traversal, integrated OpenXR acceptance, complete tile interaction, retail UI parity, uninterrupted campaign continuity, neighboring-world streaming, and visual gates remain |
| Fallout 3 | Owned-profile menu/intro/Escape convergence and persistent CG00 sex/name/appearance through stage 62 work; later state contracts validate, but FPS/Hex/VR, authored trigger execution, dialogue/KF, actors, and Vault 101 remain |
| TTW | Profile inspection works; runtime support, including archive/loose-file/script/world-transition compilation, is absent |
| JAM | Dependency registrar plus bounded JVS sprint and JBT time-dilation semantics work; missing dependencies and portable xNVSE/JIP/JohnnyGuitar/kNVSE/Stewie/UIO/JAM semantics keep the launcher toggle disabled |

The runtime manifest is the executable truth. Documentation may describe this
sequence, but a route stays disabled until its direct gate passes.
