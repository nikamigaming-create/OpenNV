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

| Route | Available now | Explicit boundary |
| --- | --- | --- |
| Fallout 1 Hex | Registered-cache launcher route through the OpenNV menu, character picker, owned Overseer movie, and bounded Godot V13ENT/Vault 13 slice | Only V13ENT is playable; this is not the complete Fallout 1 campaign |
| Fallout 1 FPS | The same Vault Dweller and save in the bounded V13ENT slice, with free movement and shooting | The FPS adapter does not extend campaign coverage beyond V13ENT |
| Fallout 1 VR | Shared-state V13ENT adapter with simulator coverage | Not launcher-enabled or physical-headset accepted; campaign-native hands, weapon, and UI remain open |
| Fallout 2 Hex/FPS/VR | The owned DAT2 Map 126 graph now compiles into a hash-verified Godot 3D scene with exact floor patches and placed-object FRM planes | The proof is headless scene construction, not rendered or interactive play; character flow, collision, gameplay, and saves are absent, so all three modes stay disabled |
| New Vegas | Owned menu, skippable intro, Doc Mitchell house/opening state, and the production Goodsprings active set with the reciprocal Doc Mitchell house/exterior exit; original flat and experimental OpenXR routes are launchable | Hex is absent; OpenXR is software-gated but not physical-headset accepted; the uninterrupted full campaign is unproven |
| Fallout 3 | Owned main menu, intro, and CG00 profile through stage-62 appearance persistence and source-backed `CG00PlayerSection4` package activation | KF playback and the stage-65 parent race/FaceGen commands remain blocked; no Vault 101 world runtime exists |
| TTW | Local profile inspection/registration only | Runtime support is absent and the edition remains disabled |
| JAM | Dependency/profile inspection plus one bounded JVS sprint semantic | The full dependency and portable-semantic gates are incomplete, so JAM remains disabled |

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
opening state as watching it through. The separately promoted production
Goodsprings active set includes the reciprocal Doc Mitchell house/exterior
exit. For Fallout
1, select **Register Fallout 1
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
The owned `CG00PlayerSection4` package and marker now persist as active at stage
62. KF playback and the stage-65 `MatchRace`/`MatchFaceGeometry` commands,
compiled Godot Vault 101 scene, and remaining opening interpreter are active
work. TTW and JAM
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
creation, script execution, gameplay/save state, and launcher-ready presentations
remain absent, so the launcher choices stay disabled.

The next local-only compiler decodes only Map 126's admitted floor/roof tile
frames and placed-object frame/rotation pairs with the owned `color.pal`:

```powershell
python .\content\tools\prepare_fo2_temple_presentation.py `
  --profile "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\fallout2-profile.json" `
  --source-manifest "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-of-trials-v1.json" `
  --output-root "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1"
```

That disposable cache contains hash-bound PNGs and an asset-free provenance
manifest. It is local derived content, is never distributed, and does not make
any launcher mode runtime-ready. The runtime can validate the complete
cache/source/profile/recipe chain and construct Map 126 in Godot's 3D hex space:

```powershell
$Godot = '<path-to-Godot-4.7.2-Mono-console.exe>'
& $Godot --headless --path .\runtime -- `
  --fo2-temple-cache "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json" `
  --fo2-temple-build-proof `
  --report "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\temple-runtime.json"
```

That proof builds the exact admitted floor and top-level object planes but does
not claim a rendered frame, collision, interaction, character flow, or playability.

## Character path is a real choice

Choose the path **before creating a character**. Each choice has its own
profile and save boundary.

| Path | Character | JAM rule |
| --- | --- | --- |
| Fallout 1 | One Vault Dweller state shared by hex, FPS, and eventually VR presentations | Separate from the Gamebryo-family profiles. |
| Fallout 2 | One future Chosen One state shared by hex, FPS, and VR presentations | Source registered separately; no runtime/save is promoted yet. |
| New Vegas | Separate standalone Mojave character | Base route today; JAM remains disabled until its dependencies and portable semantics pass. |
| Fallout 3 | Separate standalone Capital Wasteland character | Standalone CG00 profile today; TTW is a future separate path and is currently disabled. |
| TTW | One future Capital Wasteland-to-Mojave character | Runtime support is absent. It cannot later adopt an existing standalone save. |

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
and promoted OpenXR play are not connected. A simulator-only adapter reaches
the shared V13ENT state but remains launcher-disabled. This route now begins at a functional, asset-free
OpenNV Fallout-style menu before the owned character picker. Fallout 1's retail
startup logos and exact retail menu art/presentation are not implemented.

Separate from the production Doc exit slice, the New Vegas saloon/exterior
sandbox is playable, not only a renderer. Its hash-pinned retail baseline
resolves 228 interior/exterior
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
production Goodsprings active set and reciprocal Doc Mitchell house/exterior
exit are also interactive. An uninterrupted whole-campaign route remains
unproven. See the
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
