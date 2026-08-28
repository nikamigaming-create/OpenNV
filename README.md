# Open Nevada

**Open Nevada** (OpenNV) is an independent Godot-based runtime, direct retail
content pipeline, and cross-platform launcher for worlds built from game assets
that a player legally owns. Its engine, interface, product identity, and
launcher contract are Open Nevada's own.

> **Live:** **[opennevada.com](https://opennevada.com)** is the public Open
> Nevada home. **[opennv.org](https://opennv.org)** is the short technical and
> community address, permanently forwarding to the canonical site.

Playable runtime downloads are paused while the new first-party Godot runtime
passes its promotion gates. GitHub remains the source and future release
authority; archived previews are not the current runtime.

![Open Nevada atlas visual](desktop/assets/open-nevada-atlas-hero-v1.png)

The first-party desktop launcher has a portable Electron shell for Windows,
macOS, and Linux. It now reads the in-repository Godot runtime manifest directly
instead of using a Windows-only engine bridge. Runtime builds are promoted per
platform only after the same campaign and compatibility tests pass. See [the
launcher architecture](docs/desktop-launcher.md).
See [the domain deployment plan](docs/domains.md) and [Cloudflare Pages
handoff](docs/cloudflare-pages.md) before publishing the public site or a
redirect.

Open Nevada ships no commercial game assets, DLC, conversion output, or
third-party mod archives. Players provide those from lawful sources.

## Current route truth

The repository now exposes all intended product routes in one launcher contract,
but it enables only routes whose ordinary launcher-to-runtime handoff is proven.

| Route | Current state | First-slice target |
| --- | --- | --- |
| Fallout 1 hex tactical | Launcher-ready after the player registers generated `hex-scene.json` and `character-start.json` | Original-style creator and Overseer movie, then source-backed V13ENT tactical play |
| Fallout 1 FPS | Launcher-ready from the same registered cache and Vault Dweller save | Same V13ENT state with free movement and shooting |
| Fallout 1 OpenXR | Pending | The shared Fallout 1 state through a headset-accepted input/presentation adapter |
| Fallout 2 Hex/FPS/OpenXR | Legally owned DAT2 install registered; the exact Temple MAP header/elevation, entry marker, 567 placed objects, 37 PRO identities, and 34 FRM identities compile to an asset-free manifest; no runtime mode is enabled | Consume that source graph in Godot, then implement character creation and one Chosen One gameplay/save state |
| New Vegas | Launcher-ready experimental route; full front-end-to-Goodsprings gate remains active | Owned main menu and skippable intro, Doc Mitchell character creation, authored exit, Goodsprings exterior |
| Fallout 3 | Launcher-ready bounded CG00 route through source-backed appearance acceptance at stage 62; player-package execution and Vault 101 scene loading remain active work | Owned front end, birth/character sequence, Vault 101 exit, first exterior save/reload |
| TTW | Local profile registration and launcher manifest selection work; runtime compilation is not implemented | A separately generated combined-world profile and new character |
| JAM | Local dependency/script registration works; the authored JAM 4.6 JVS Shift/75% forward-sprint speed is transported as one bounded desktop capability, while missing packages and all other semantics remain explicit and the toggle stays disabled | User-installed JAM profile with every required command/event/UI behavior accounted for |

“Local slice works” is not the same as “launcher-ready,” and “first slice” is
not a whole-campaign claim. The runtime manifest is the authority used to keep
those distinctions visible.

## Run the source launcher

Install the Electron dependencies once, then use the repository start command:

```powershell
Push-Location desktop
npm install
Pop-Location
.\scripts\Start-OpenNV.ps1
```

If Godot is not found automatically, pass the Godot 4.7.2 Mono executable with
`-Godot`. Select **New Vegas** and **Launch** for the normal owned main menu;
**New Game** plays the owned intro, and `Escape` skips into the same Doc Mitchell
opening state as watching it through. For Fallout 1, select **Register Fallout 1
cache**, choose the generated `hex-scene.json` and then `character-start.json`,
choose Hex Tactical or First Person, and launch. Registration stores local paths
and the character-start hash; it does not copy or package owned content.

Fallout 3 registration is available separately and writes a local profile under
`%LOCALAPPDATA%\OpenNV\profiles\fallout3\vanilla` by default:

```powershell
.\scripts\Register-OpenNVFallout3.ps1 `
  -Fallout3Root 'D:\SteamLibrary\steamapps\common\Fallout 3 goty'
```

That command resolves the owned menu, movies, quest chain, birth inputs, and
Vault 101 resource graph. The launcher can boot the bounded CG00 sex/name flow,
resume its stage-60 character, select from source-backed playable race and
sex-aware hair/eye records, and persist the owned FaceGen defaults at stage 62.
The preview is an exact owned-texture inspection surface, not a 3D face render.
The `CG00PlayerSection4` package runtime, compiled Godot Vault 101 scene, and
remaining opening command interpreter are still active work. TTW and JAM
registration are documented in [the mod policy](docs/mods.md);
registration alone does not make either route runtime-playable.

Fallout 2 source registration is also available and writes only a small local
manifest; it does not extract or copy the three owned DAT2 archives:

```powershell
.\scripts\Register-OpenNVFallout2.ps1 `
  -Fallout2Root 'D:\SteamLibrary\steamapps\common\Fallout 2'
```

The fourth launcher card then reports the owned install as registered while its
Hex, FPS, and VR choices remain disabled. Compile the bounded owned Temple
source graph separately; the output remains local and contains identities and
authored numeric data, not extracted assets:

```powershell
python .\content\tools\fo2_first_slice.py `
  --profile "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\fallout2-profile.json" `
  --output "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-of-trials-v1.json"
```

This resolves Map 126 (`Arroyo Temple` / `artemple`), its MAP-header entry tile
and rotation, exact elevation grid, scripts, placed object graph, and required
PRO/FRM hashes through patch → critter → master overlay precedence. Character
creation, script execution, gameplay/save state, and every Godot presentation
remain absent, so the launcher choices stay disabled.

## Character path is a real choice

Choose the path **before creating a character**. Each choice has its own
profile and save boundary.

| Path | Character | JAM rule |
| --- | --- | --- |
| Fallout 1 | One Vault Dweller state shared by hex, FPS, and eventually VR presentations | Separate from the Gamebryo-family profiles. |
| Fallout 2 | One future Chosen One state shared by hex, FPS, and VR presentations | Source registered separately; no runtime/save is promoted yet. |
| New Vegas | Separate standalone Mojave character | Start base and add JAM later, or begin with JAM. Keep JAM enabled after a save uses it. |
| Fallout 3 | Separate standalone Capital Wasteland character | Vanilla standalone route. Choose TTW at character creation if the character should continue into the combined world. |
| TTW | One Capital Wasteland-to-Mojave character | A separate combined-world path, base or JAM. It cannot be retrofitted onto an existing standalone save. |

This makes the important distinction visible rather than hiding it in mod
files: JAM is modular; TTW is a character-path decision.

## Mod support without a Windows-only ceiling

Open Nevada accepts content and mod sources through isolated profiles rather
than touching a game installation. A Windows-only native plugin is not a
product-level exclusion. It enters a compatibility pipeline:

1. record the extension behavior and its needed events/commands;
2. implement a portable OpenNV semantic contract in the runtime;
3. run the real mod through a recorded launch validation;
4. promote it to *supported* only when that behavior is reproducible.

That is how major extender-dependent mods can work across platforms without
pretending that an arbitrary Windows DLL is safe to load into a different
runtime. The current catalog distinguishes validated modules from ones still
waiting on an extender bridge. See [the mod policy](docs/mods.md).

## Current Godot development slices

Fallout 1 has a bounded owned-data V13ENT slice with original-style character
creation, the owned Overseer movie, Escape/skip convergence, one shared save,
hex-tactical play, and FPS movement/shooting. The desktop launcher validates and
registers the two generated local cache contracts, passes their paths and hash to
Godot, and owns an isolated Vault Dweller save. Only V13ENT is playable; the
other 95 inventoried maps, full dialogue/quest simulation, combat-formula parity,
and OpenXR are not connected. This route now begins at a functional, asset-free
OpenNV Fallout-style menu before the owned character picker. Fallout 1's retail
startup logos and exact retail menu art/presentation are not implemented.

The current checked-in slice is a playable Goodsprings sandbox, not only a
renderer. The current hash-pinned retail baseline resolves 228 interior/exterior
assets, 504 enabled placements, 379 textures, 476 materials, 97 authored saloon
pickups, five containers, 27 lights, and a
reciprocal XTEL pair joining the saloon to WastelandNV cell `[-17,0]`. LAND
geometry and its 24 authored texture layers form the exterior ground. Sunny
Smiles and the seated settler load inside; Easy Pete loads at his exterior ACHR.
The promoted route collects the saloon's real `.357`, fires using
its retail damage/clip/ammo profile, takes an authored Beer, loots a resolved
authored crate, opens both sides of the linked door, walks and shoots through
the opening, autosaves, exits, and restores the exact state in a second process.

CI pins the official Godot 4.7.2 Mono Windows archive by SHA-256
`a2a48473a7414c5f19fab690518caebb738c09ef9601f6bd2388676a7f53b3c0`.

```powershell
python -m pip install -r content/requirements-build.txt
.\scripts\Test-GodotRuntime.ps1
.\scripts\Test-GodotRuntime.ps1 `
  -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
```

`Build-GodotRuntime.ps1` packages the legal-content helper with the experimental
Windows export. The resulting `OpenNV.exe` lets a player select their owned
Fallout: New Vegas installation folder or its `Data` folder directly; it does
not require Python or another engine at runtime.

The New Vegas owned front end and Doc Mitchell opening are implemented as a
bounded campaign-state route. New Game plays the owned intro and Escape skips
to the same opening state; Continue/Load use the canonical save owner. The
ordinary uninterrupted menu-to-Doc-exit-to-Goodsprings proof remains the active
gate, so this is still not the full New Vegas campaign. See the
[canonical whole-game delivery plan](docs/whole-game-delivery-plan.md),
[multi-game first-slice plan](docs/multi-game-first-slices.md),
[single-page architecture](docs/architecture.md),
[data and configuration accountability contract](docs/data-and-configuration-accountability.md),
[installation status](docs/installation.md), [clean implementation boundary](docs/clean-room.md),
and [release policy](docs/nightlies.md).

Flat play and OpenXR are first-class modes over one shared game/save state. The
OpenXR software path is launchable with a bounded Meta Touch action map,
metre-correct rig, two owned-data retail hands, controller locomotion/actions,
haptics, and a wrist HUD. The repo-local simulator passes both sticks, snap
turn, door/fire/reload/save, supported eye height, and native stereo capture. A
connected-headset final-eye validation is still required before calling VR ready.
The owned-data Saloon slice also includes an experimental practice pool table:
the intact retail table triangles, authored cue/rack/four placed balls, NIF
convex bodies, shared flat/OpenXR strike simulation, and v2 save state are
software-gated. Full eight-ball rules and physical headset acceptance are not
claimed.

## Release contents

Future runtime releases will contain the exported Godot runtime, direct content
contracts, launcher, and source-revision metadata. They will not contain:

- commercial game files, DLC, or conversion output;
- third-party mod archives or downloader credentials;
- a player's saves, profiles, or mod-manager state.

No playable Godot runtime is currently published. Historical preview archives
retain the notices that applied when they were built; see [NOTICE.md](NOTICE.md).
