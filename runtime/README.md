# OpenNV Godot runtime

This is the first-party Open Nevada runtime. It uses Godot Forward+ and accepts
only artifacts produced by the direct retail-content pipeline in `../content`.

The current slice reads the owned master directly and runs a playable,
recipe-pinned Goodsprings sandbox. It loads 228 interior/exterior assets, 504
enabled references, 379 textures, 476 materials, 97 saloon pickups, five containers, 27
authored lights, Sunny Smiles, the enabled saloon settler, and exterior Easy
Pete. The reciprocal saloon XTEL pair aligns the actual door planes and joins a
bounded WastelandNV exterior containing LAND `000db010`.
The incoming XTEL owns the spawn. The `.357` pickup uses retail damage and clip
data; inventory, ammo, objective, removed pickups, and door state autosave and
cold-reload. One fully resolved crate is transferable; containers backed by
unimplemented leveled-list records stay explicitly locked. It does not claim
AI/package simulation, damageable combat, simulated projectiles, or a complete campaign.

Run the complete repository gate from the repository root:

```powershell
pwsh -File scripts/Test-GodotRuntime.ps1
```

Pass `-FalloutNewVegasData` to make the gate validate the owned master and BSA,
extract the model directly, build a temporary cache, load it in Godot, and
delete the cache afterward. No retail-derived file or generated conversion
belongs in Git.

The whole-game CELL path is partitioned separately from the bounded Goodsprings
sandbox. Given the immutable CELL corpus and compile plan, compile, validate,
and load one CELL without any hand-authored scene data:

```powershell
pwsh -File scripts/Test-OpenNVStaticCellSlice.ps1 `
  -DataRoot "D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data" `
  -CorpusRoot "D:\Builds\OpenNV-cell-parity-corpus-20260824-r6" `
  -PlanRoot "D:\Builds\OpenNV-cell-compile-plan-20260824-r2" `
  -CellFormKey "DeadMoney.esm:0102c7" `
  -OutputRoot "D:\Builds\OpenNV-light-cell-DeadMoney-0102c7-run1" `
  -RuntimeReport "D:\Builds\OpenNV-light-cell-DeadMoney-0102c7-run1.json"
```

That gate refuses overwrites, requires zero blockers, verifies all owned source
and generated hashes, and proves typed Godot instantiation. The example contains
three placed `LIGH` references and therefore creates three authored point lights
without a mesh asset. Static-model cells also build authored collision when all
of their exact REFR semantics are supported. The report deliberately remains
`playable=false` and `parity=false` until the remaining CELL capability families
and matched retail evidence pass.

For the strict full-layout LAND proof, use the same command with CELL
`FalloutNV.esm:0ddb26` and fresh output/report paths. That target resolves one
direct LAND child into one 1,089-vertex terrain mesh, one deterministic baked
diffuse texture, and one Godot collision mesh. Partial/default LAND layouts are
still explicit blockers; a successful terrain load is not a weather, lighting,
streaming, gameplay, or retail-visual-parity claim.

Actor parity captures require `--cell-scene`, an actor scene/set, `--capture-root`,
and the compact oracle artifact supplied as `--retail-state-contract`. The
runtime rejects a missing/mismatched ACHR, shot set, pose, geometry gate, or
projection label. A provisional retail FOV may improve a failing comparison but
cannot promote exact projection parity.

Cell scene v13 and actor scene v5 are required. Older caches omit full authored
rotation/scale, configuration identity, deterministic outfit resolution,
current actor sidecars, or the owned-data first-person rig and are rejected.
Campaign save v6 retains v1-v5 load compatibility, persists source-derived
Level/HP/AP/XP, and keeps the v5 opened-container remaining counts. The current
container view supports Take One, Take All, and Exit, while player-to-container
deposits and integrated OpenXR input remain open.

Build an asset-free experimental Windows archive after installing the pinned
Godot Mono export templates and `content/requirements-build.txt`:

```powershell
pwsh -File scripts/Build-GodotRuntime.ps1 -OutputRoot D:\Builds\OpenNV
```

The archive contains the Godot executable and a packaged legal-content helper,
but no commercial content. On first launch, select a legal Fallout: New Vegas
`Data` folder; OpenNV prepares its private cache and enters the owned New Vegas
menu, whose New Game/Continue route begins in Doc Mitchell's house with the
Goodsprings exterior and saloon eagerly instantiated as a bounded composite.
The source-portal active set keeps only the authoritative current CELL active;
linked CELLs stay loaded but hidden, nonprocessing, and noncollidable until a
source portal transition. Python and
OpenMW are not required on the player's machine. Later
launches reopen that verified cache automatically.

The owned character menus now bind the prepared `TextEditMenu` and
`RaceSexMenu` backgrounds. The current ordinary stage-10 capture is
`D:\Builds\OpenNV-fnv-doc-opening-20260829-r3-owned-menu-nav\stage10-owned-menu-nav.png`,
SHA-256
`5096233bbcf0293191c83dd4fbdaf0ce5f5d3aed16ba380eb31f1c3d7e744c28`.
Doc's startup placement no longer adds the raw chair-reference elevation. The
native proof at `D:\Builds\OpenNV-fnv-furniture-occupancy-proof-20260829-r1`
preserves his authored ACHR transform while occupying FURN `001059b0`, releases
that occupancy for the stage-40 package, and reaches the stage-55 checkpoint.
Exact seated-loop, furniture-entry, and furniture-exit visuals remain
unsupported. The native
`D:\Builds\OpenNV-fnv-cigarette-proof-20260829-r2` proof shows source cigarette
`ANIO 00083519` default-hidden, visible only for its exact `IDLE 00071ee3`, and
hidden on idle exit. Its owned NIF contains no particle/effect owner, and no
other source-backed smoke emitter was found, so visible smoke is not
implemented.

The following controls and loadout describe the independently accepted saloon
component, not the complete default Doc/exterior/saloon route. Use WASD and
mouse-look, press E to pick up items, open containers, or operate a
door, left-click to fire, R to reload, and F5 to save. Flat and XR both start
with the owned-data 10mm equipped. The main door opens
both reciprocal references and can be crossed without a loading screen. The HUD tracks the
four-stage sandbox objective and inventory. Press Tab to open the shared Pip-Boy view:
Status, Items, Data, Map, and Controls all read the same campaign/session snapshot, and
Escape closes it without advancing the world. The save also retains the ordinary player
transform after the authored world context is loaded, so Continue restores the saved
position as well as inventory, doors, and objectives. Packaging proves the route and a
separate cold reload before accepting the build.

The same standalone sandbox component has an experimental OpenXR diagnostic.
Its simulator result does not promote the launcher's default joined route;
**OpenXR mode** there remains explicitly experimental and awaits integrated
route acceptance. Oculus Touch and the OpenXR
1.1 generic-controller fallback are declared: left stick moves, right stick
snap-turns, right grip activates, right trigger fires with haptics, B reloads,
and X saves. VR starts with the owned master-record 10mm pistol profile equipped,
one full magazine, and one reserve magazine. The tracked eye is calibrated once
to 1.68 metres above the authored floor. The wrist Pip-Boy screen is an actual OpenXR
world-space pixel surface attached to the left hand; it consumes the same UI snapshot
as flat mode. Legal `lefthand1st.nif` and `righthand1st.nif` assets provide the two
visible skinned hands; grip poses own their transforms and aim poses own rays.
The repo-local simulator passes tracking, both sticks, locomotion, snap turn,
door activation, fire, reload, save, supported eye height, and native stereo
capture. This corrected path remains pending a physical-headset rerun. This
component path is Windows PCVR; a standalone Quest APK/export/install gate is not
implemented yet.

Add `-FalloutNewVegasData <path>` to the build command for a local end-to-end
gate of the exported executable, packaged helper, legal cache, and Godot load.

The bounded Fallout 3 front end launches from a locally registered owned GOTY
profile:

```powershell
.\scripts\Register-OpenNVFallout3.ps1 `
  -Fallout3Root 'D:\SteamLibrary\steamapps\common\Fallout 3 goty'

Godot_v4.7.2-stable_mono_win64.exe --xr-mode off --path runtime -- `
  --fo3-profile "$env:LOCALAPPDATA\OpenNV\profiles\fallout3\vanilla\fallout3-profile.json"
```

Registration hashes the owned master, archives, menu inputs, music, quest
graph, and movies, and converts the owned intro into a disposable local Theora
cache. The runtime verifies that complete source/output join before showing the
menu. **New Game** plays the intro; Escape and **Skip** enter the same owned CG00
sex/name path. The resulting profile-bound character save can be continued at
stage 60. The source-backed appearance route exposes the owned playable races,
sex-aware hair and eye lists, and Player-plus-RACE FaceGen defaults, then saves
the accepted choice at stage 62. Its deterministic preview shows the exact
owned head/hair/eye source textures; it is not yet a 3D FaceGen actor render.
The bounded normal route now enters the owned Vault 101 birth room, plays the
exact stage-65 Dad cue, applies stages 80 and 85, reaches stage 90 INFO
`0001f379`, and advances through seven exact stage-100 commands with cold
restore. A fresh pinned-Theora profile also compiles the exact CG01 stage-0 and
stage-5 results plus the sex-specific two-line Dad dialogue into stage 10. The
same profile compiler now resolves objective 10 (`Walk to Dad.`), the exact
source `SCPT`→`ACTI`→`REFR 0002ea54` trigger/box transform, and all four
stage-12 commands. A native acceptance executor applies that trigger state and
cold-restores stage 12 at
`D:\Builds\OpenNV-fo3-cg01-stage12-20260829-r1`. Its profile SHA-256 is
`a77692f69cf958d769f93b96dc62554b48e5b12d34e8850a8fc14f91181f3704`;
the apply/restore report SHA-256 values are
`5b440b505a9d02549b976e49bd3c49392fc712f4b5b1b5aeb3195b18941be98c`
and `3996e216be77b57200cb5201a216ff04ce39e3d0dd4f5e75a67894870f4df614`.
This does not implement normal-world toddler locomotion or physical trigger
entry, CG01 Dad presentation/response, general package/KF/dialogue AI, or a
freely playable Vault 101 world; the ordinary route still stops at stage 10.

The isolated TTW executor applies and cold-restores the exact owned effective-
source stages `CG00:0/60/100` and `CG01:0/5`, including the synchronously nested
stage 5, with 38 commands and matching state SHA-256
`a4d3b74e5d7e4a83c409138e377aa17ac54d7387b6a23f2c5e6e5db1c7d53e58`.
It uses dedicated `ttw:` save and `ttw-fo3-opening:` cache identities and keeps
`runtimeReady=false`. The ignored local reports are
`local/ttw-fo3-runtime-proof/apply.json` and `restore.json`. Their exact blockers
are `ttw-vault101-cell-resource-compilation`,
`reference-transform-and-world-application`,
`owned-movie-runtime-transcode-and-playback`,
`cg01-stage-10-and-later-gameplay`, and
`xnvse-and-jam-native-plugin-execution`.

The private Fallout 1 tactical slice launches from a prepared, ignored owned
cache with:

```powershell
Godot_v4.7.2-stable_mono_win64.exe --xr-mode off --path runtime -- `
  --fo1-hex-scene <cache>\hex-scene.json --save-path <cache>\v13ent-hex-save.json
```

The bounded Fallout 1 new-game route adds the hash-pinned owned character/opening
cache. It begins at a functional asset-free, original-style menu whose **New
Game** action opens the owned original picker with Max Stone, Natalia, Albert,
and Custom; **Exit** quits, and the picker's Back action returns to that menu.
Take selects a premade, while Modify loads it into the complete
SPECIAL/skills/traits editor. It then shows the complete Overseer briefing before
entering the same tactical session. The movie's **SKIP** button or `Escape`
converges on the same final-frame fade into live first-person control at exact
V13ENT tile `17690`, rotation `2`. The menu itself is an OpenNV adaptation; the
picker, creator chrome, and movie are verified owned Fallout data. The door
remains open as a labeled presentation adaptation for the Vault 13 corridor
look-back:

```powershell
Godot_v4.7.2-stable_mono_win64.exe --xr-mode off --path runtime -- `
  --fo1-hex-scene <hex-cache>\hex-scene.json `
  --fo1-new-game `
  --fo1-character-start <start-cache>\character-start.json `
  --fo1-character-start-sha256 <manifest-sha256> `
  --fo1-start-presentation hex-tactical `
  --save-path <cache>\v13ent-new-game-save.json
```

`--fo1-start-presentation` accepts `hex-tactical` or `first-person`. Both enter
through the same verified movie-to-live first-person seam and then release
control in the selected view over the same save/session state.

First-person uses captured mouse look, Escape to release the cursor, click to
recapture/fire, and continuous WASD/arrows movement constrained by the source
walk mask. `C` cycles
first-person → tactical → third-person → first-person. Tactical uses MMB
orbit/tilt, RMB drag-pan, wheel zoom and WASD/arrows/edge pan. `F` focuses the
player, `Home` resets the entry-to-door route,
left-click path movement/target selection, `Tab` hostile cycle and auto-frame,
double-click or `X` attack, `G` exact walkable-hex overlay, `V` source
floor/scenery, `B` experimental 3D topology blockout, `Space` end turn/rat
turn, and `F5` save. `P` opens the owned Fallout 1 Pip-Boy 2000 with live
Status, Automaps, and Archives pages. The default view uses one opaque continuous floor over all
30,196 source-backed movement hexes, a locally imported animated 3D Vault
13-suited player, twenty regrounded animated 3D giant rats, and 311 source-driven
owned cave/Vault/corpse instances, including the source-axis cave-to-Vault
threshold frame. A shader-driven camera melt opens sightlines
to the player and selected rat. Tactical projection removes the enclosure and
Vault corridor above the floor; the presentation-footprint gate excludes 1,608
otherwise floor-backed hexes, leaving 27,519 legal grid hexes and 86,841 unique
depth-tested edges. `V` swaps to the cleaned source floor/sprite reference in
tactical mode; first-person always suppresses those 2.5D cards and retains the
owned continuous floor. `B` exposes the rough topology diagnostic. None of
these diagnostics changes gameplay authority.

On this development machine the verified one-click launcher is
`dist/fo1-v13ent-playable-20260826-r7/Play-Fallout1-New-Game-3D.cmd`.
It hash-checks scene `da6e7221...47cc83db`, the embedded runtime profile, and the character-start contract before
launching; it contains no retail assets and is not a portable release package.

This route uses the owned original creator/opening, scripted first-run spawn,
exact Fallout 1 hex/object and
rat-combat authority, plus owned New Vegas assets only as a private 3D
presentation layer. The bounded route decodes its starting knife, 10mm Pistol,
magazine/reserve ammunition, stimpaks, and flares from the owned V13 script and
PRO records. FPS and tactical views share weapon, ammunition, reload, HP, death,
and save state; both ranged and melee attacks can kill source rats. Ranged
presentation includes tracers, impacts, ricochets, physically grounded casings,
and owned donor audio, while third person/tactical mode displays the equipped
weapon. Rat activation is local (six exact hexes), not whole-cave aggro. It is a
playable `V13ENT` combat vertical slice, not a full Fallout campaign, complete
retail critical/armor/AI parity, or an OpenXR claim.

The bounded V13ENT OpenXR adapter can be exercised directly without enabling
the launcher route:

```powershell
.\scripts\Test-Fo1XrSimulatorControls.ps1 `
  -Godot <Godot-4.7.2-Mono-console.exe> `
  -Scene <hex-cache>\hex-scene.json `
  -SimulatorRuntimeManifest <simulator>\openxr_simulator.json `
  -OutputDirectory <new-empty-proof-directory>
```

It reads Oculus Touch/generic grip, aim, move, turn, fire, reload, and save
actions and publishes accepted changes through the same `Fo1TacticalSession`
used by flat first person. The 2026-08-28 durable-simulator run proved both
tracked controllers, 1.360 m of source-mask locomotion, two 30-degree snap
turns with 0 m horizontal HMD-pivot error, and authoritative fire. Its blocking
gate stayed failed because the installed simulator did not map actions named
`reload` and `save` to its B/X state. V13ENT also has no supported XR door-use,
visible first-person hand, grip-mounted weapon, or campaign-native wrist-UI
contract, and no physical headset was accepted. This is a truthful shared-state
adapter and diagnostic gate, not a launchable Fallout 1 VR mode.

The scene cache embeds a hash-pinned `opennv-fo1-runtime-profile-recipe/v1`.
`Fo1RuntimeProfile` parses it once and supplies typed camera, atmosphere,
gameplay-adaptation, mob-presentation, cutaway, and showcase contracts to the
runtime. Missing or invalid values stop loading; these systems do not silently
fall back to V13ENT tuning compiled into C#.
