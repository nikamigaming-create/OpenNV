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
| Fallout 1 Hex | Registered-cache route through the OpenNV menu, character picker, owned Overseer movie, and bounded Godot V13ENT/Vault 13 slice. The selected premade/custom identity, sex, presentation authority, and weapon-visual suppression policy are wired through save/cold restore into the gameplay actor. **Continue** bypasses the picker/movie/entry reset and restores the saved hex plus finite Hex/FPS/shoulder camera mode, yaw, pitch, and zoom before controls. Every 3D classic humanoid requires the same verified owned FNV full-body donor preview set, with both male and female variants. | GCD/FRM data remains the classic identity/gameplay authority; the FNV body is presentation only and is not a parity claim. A missing, malformed, wrong-sex, body-role, outfit, hash, or socket donor fails closed—there is no procedural, FRM-player, silhouette, or standee substitute. Saves without the complete player-presentation identity and `opennv-fo1-camera-state/v1` camera state are non-continuable. Only V13ENT is playable; fresh producer/native visual acceptance remains pending. |
| Fallout 1 FPS | The same selected Vault Dweller identity and save in the bounded V13ENT slice, with free movement and shooting; Continue restores the saved first-person camera state before input is released. | The FPS adapter does not extend campaign coverage beyond V13ENT; the same shared-donor and fail-closed save boundaries apply. |
| Fallout 1 VR | Shared-state V13ENT adapter with simulator coverage | Not launcher-enabled or physical-headset accepted; campaign-native hands, weapon, and UI remain open |
| Fallout 2 Hex | Registered-profile route into the original owned 640×480 Narg/Mingan/Chitsa selector plus source-backed Modify/Create. The selector and Map 3 gameplay actor use the same verified owned FNV full-body donor preview set; it must supply the selected sex plus the hash-bound body, outfit, and socket join. The opening Elder movie's natural end and Skip converge through the same exact terminal source frame/fade, black handoff, prepared live camera, and reveal. Two bounded branches now retain the same selected identity: tagged-Speech Cameron → live ARVILLAG input/save, or Temple guardian AP combat → exact Spear loot/equip/save. | GCD/FRM remains identity/gameplay authority and the donor is non-parity presentation. A missing or incompatible donor fails closed; there is no procedural, FRM-player, silhouette, or standee fallback. The branches are alternatives: no dead-guardian shortcut to ARVILLAG exists. Torch animation/smoke, target AI/turns, general INT execution, full campaign, FPS/OpenXR, and retail parity remain absent. See the [canonical FO2 branch ledger](docs/evidence/fo2-first-slice-branch-ledger.md). |
| New Vegas | Owned menu and skippable intro; source-ordered Doc opening; exact chair reference `001059b0`; exact patient-bed `REFR 00103e5b` → `FURN 00106a6a` (`NVbedtwin01`) identity; owned cigarette `ANIO 00083519` attached to `Bip01 R Hand`; visible name entry; selection-keyed default male and female full-body FaceGen preview contracts with all 43 admitted CTL/EGM controls; exact stage-40 chair-exit root transfer; and stage-55 checkpoint autosave. | The male/female preview change is code-complete but has no regenerated owned cache or native acceptance; non-default race/hair/eye combinations deliberately fall back to owned source-texture tiles. The exact bed binding likewise awaits a fresh native/retail differential. The admitted cigarette NIF/KF contains no source particle/effect emitter, so visible puffs remain an explicitly first-party, tip-anchored non-parity adaptation. Exact phase/pixels, complete package AI, uninterrupted campaign continuity, integrated OpenXR, Hex, and physical-headset acceptance remain unproven. |
| Fallout 3 | Owned main menu, intro, sex/name/appearance selection, persistent CG00 progression, and bounded CG01 state/save work. Source `PACK` sections now select their hash-bound KF sequences, and the player view composes the sampled `Camera1st` node through its owned parent chain without treating it as a `NiCamera`. | Those source-backed implementation fixes are not a retail-input proof: the toddler acceptance path still auto-steers, and ordinary user-driven movement/trigger entry plus a matched retail differential remain gated. No freely playable Vault 101 route, general package/dialogue interpreter, Hex, VR, or retail parity is claimed. |
| TTW | The launcher exposes two editions rather than a fifth game card: **TTW · Fallout 3 opening** under Fallout 3 and **TTW · New Vegas opening** under New Vegas. A strict local profile and records-only effective-source adapter feed the bounded TTW-FO3 CG00→CG01-stage-5 compiler/executor with dedicated cache/save identities. | The currently consumed adapter resolves effective records only; archive/loose resource winners are not yet connected to either playable world. TTW-FNV has no effective-stack Doc profile/runtime. Both editions remain disabled, and xNVSE/JAM plugin execution is unsupported. |
| JAM / xNVSE | Dependency/profile inspection plus bounded first-party transport of JVS sprint and JBT time-dilation settings/semantics | OpenNV does not load native mod DLLs. Portable xNVSE, JIP LN, JohnnyGuitar, kNVSE, Stewie Tweaks, UIO, and the remaining JAM script/event/UI/AP/animation/audio/cosave semantics are unsupported, so JAM remains disabled. |

“Local slice works” is not the same as “launcher-ready,” and “first slice” is
not a whole-campaign claim. The runtime manifest is the authority used to keep
those distinctions visible.

New Vegas preparation is family-scoped rather than a periodic full-cache
rebuild. Static, CELL, opening, and actor outputs have separate identities. The
actor family hashes the exact ordered one-level actor recipe route (recipe IDs
plus hashes), so actor add/remove/reorder/content changes invalidate it while an
unrelated CELL presentation edit does not. Restore is read-only and never starts
a compiler; only explicit preparation rebuilds a changed family and its actual
dependents.

## Run the source launcher

Install the Electron dependencies once, then use the repository start command:

```powershell
Push-Location desktop
npm install
Pop-Location
.\scripts\Start-OpenNV.ps1
```

If Godot is not found automatically, pass the Godot 4.7.2 Mono executable with
`-Godot`. The compact launcher always shows Fallout 1, Fallout 2, New Vegas,
and Fallout 3 as its four top-level choices. Selecting a card exposes the same
FPS / Hex / VR mode row and Play action below it; an unfinished route remains
visible and disabled instead of disappearing. The current manifest admits
Fallout 1 Hex/FPS, Fallout 2 Hex, and New Vegas FPS plus an experimental
OpenXR route under their separate runtime gates. Fallout 1 VR, Fallout 2
FPS/VR, New Vegas Hex, and all Fallout 3 presentations remain disabled. TTW is
an edition dropdown under Fallout 3 or New Vegas, not another top-level card.
Select **New
Vegas** and **Play** for the normal owned main menu;
**New Game** plays the owned intro, and `Escape` skips into the same Doc Mitchell
opening state as watching it through. The default owned-data cache now binds an
ordered Doc house → Goodsprings exterior → saloon chain. It aligns and links
`00103e61 ↔ 00103e69` and `0010636f ↔ 0010618e`, and exercises ray/projectile/
capsule continuity at each pair. Current-source output accepts configured flat
movement through both links and cold-restores the saved saloon state. Campaign
save v6 records saloon CELL `00106185`
and the player transform, and also persists the source-derived Level 1,
HP 200/200, AP 80/80, and XP 0/200 state used by the Pip-Boy. The current
source-portal lifecycle eagerly instantiates all three prepared spaces, keeps
only the authoritative current CELL active, and suspends every linked
noncurrent CELL's presentation/physics resources. Admitted actors are aligned
once to the active CELL's authored collision instead of retaining a floating
visual root. One current-CELL WorldEnvironment/sky owner restores
interior XCLL background/fog on interior transitions and, in the configured bounded clear-day
mode, resolves exterior `000daebb` through its owned WRLD/CLMT to unconditional
`NVWastelandClear` `000ffc88` at the exact day sample. The normal route now
renders the verified owned atmosphere/cloud models and binds four cloud texture
layers. Exterior surface and directional lighting still use the existing
provisional compiled adapter; dynamic clock/weather/global state and retail
visual parity remain open. A
fresh four-family cache now admits that
lifecycle together with exact controller-door articulation and target-local
static convex collision. The current configured-input route requires vertical
as well as horizontal convergence at intermediate NAVM edges, climbs the
source-backed saloon porch, crosses both XTEL pairs, saves in the saloon, and
cold-restores that state with zero replayed transitions. Portal setup samples
articulated doors at their synchronous closed terminal; activation rejects any
non-door collider, while an empty ray may resolve only one facing portal and
records that exact source-door identity. Sunny
`00104e85` loads once in her authored enabled
state without a proof override. OpenNV compiles the installed
`hud_main_menu.xml`, `stats_menu.xml`,
`inventory_menu.xml`, and `map_menu.xml` closures, their four selected owned
bitmap fonts, and the owned Pip-Boy background into a hash-verified gameplay-UI
contract. The HUD stays hidden until the authored Doc control policy enables
the Pip-Boy. A completed owned campaign save now has a native Godot visual
acceptance in which configured `Tab` opens the surface, all three saved opening
items are present in the authoritative snapshot, and configured `Escape` closes
it. The accepted capture uses the installed background and bitmap fonts without
missing-glyph boxes or exposing a local save path. It is a functional flat input
and populated-surface result, not a retail visual-parity claim. OpenXR consumes that same snapshot
through a status-only wrist surface and owned font/theme path; ITEMS/DATA
navigation and its full Pip-Boy input contract remain explicit headset gates. The current shell
uses verified ITEMS/DATA rectangles while STATS reuses the verified ITEMS frame
because its root rectangle still depends on unsupported Gamebryo expressions.
It does not yet interpret every tile expression or provide complete
equip/use/drop behavior, so it is not described as retail HUD/Pip-Boy parity.
For Fallout 1, select **Set up Fallout 1**, choose the generated
`hex-scene.json` and then `character-start.json`,
choose Hex Tactical or First Person, and launch. Registration stores local paths
and the character-start hash; it does not copy or package owned content. The
current launcher uses Godot's GL compatibility renderer for this bounded route
because Vulkan currently stalls before its first visible frame on the
development machine. GL reaches the Fallout menu, owned picker/movie, live
first-person, shoulder, and Hex gameplay, but reports unsupported
volumetric-fog features; it is a functional bounded-route recovery, not a match
for the supplied video's visually consistent high-fidelity cave.

Fallout 3 registration is available separately and writes a local profile under
`%LOCALAPPDATA%\OpenNV\profiles\fallout3\vanilla` by default:

```powershell
.\scripts\Register-OpenNVFallout3.ps1 `
  -Fallout3Root 'D:\SteamLibrary\steamapps\common\Fallout 3 goty'
```

That command resolves the owned menu, movies, quest chain, birth inputs, and
Vault 101 resource graph. The bounded development frontend can boot the CG00
sex/name flow, resume its stage-60 character, select from source-backed playable
race and sex-aware hair/eye records, and persist the owned FaceGen defaults at
stage 62. The preview is an exact owned-texture inspection surface, not a 3D
face render. The early-birth runtime now starts each admitted actor from its
source `PACK` section, selects the hash-bound KF sequence, and follows admitted
idle transitions. The player camera samples the owned `Camera1st` skeleton node
and parent chain using the normal Gamebryo-to-Godot conversion; it no longer
adds the incorrect camera-axis treatment that would apply to a `NiCamera`.
Those are source-backed implementation corrections, not acceptance of the
retail scene. The current toddler proof uses an internal auto-steered target,
so ordinary configured user input, physical trigger entry, actor/camera timing,
and matched retail/native frames remain fail-closed gates. The launcher keeps
every Fallout 3 presentation disabled; no freely playable Vault 101, general
package/dialogue interpreter, lip playback, Hex, VR, or parity is claimed. TTW
and JAM
registration are documented in [the mod policy](docs/mods.md);
registration alone does not make either route runtime-playable.

The owned JAM 4.6 profile currently transports two narrow first-party desktop
behaviors: Shift-held forward sprint at its authored 1.75 multiplier and
X-toggled Bullet Time at its authored 0.5 world-time multiplier. This is not an
xNVSE compatibility layer. OpenNV never loads the native mod DLLs, and portable
xNVSE, JIP LN, JohnnyGuitar, kNVSE, Stewie Tweaks, UIO, plus JAM's remaining AP,
UI, animation, audio, event, script, and cosave semantics are unsupported. JAM
therefore remains disabled.

Fallout 2 source registration is also available and writes only a small local
manifest; it does not extract or copy the three owned DAT2 archives:

```powershell
.\scripts\Register-OpenNVFallout2.ps1 `
  -Fallout2Root 'D:\SteamLibrary\steamapps\common\Fallout 2'
```

The fourth launcher card validates that profile and enables Hex only when the
matching Temple, transition, Arroyo, player, and character-start artifacts have
been prepared under `%LOCALAPPDATA%\OpenNV`. FPS and VR remain disabled. Compile
the bounded owned Temple source graph locally; the output contains identities
and authored numeric data, not extracted assets:

```powershell
python .\content\tools\fo2_first_slice.py `
  --profile "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\fallout2-profile.json" `
  --output "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-of-trials-v1.json"
```

This resolves Map 126 (`Arroyo Temple` / `artemple`), its MAP-header entry tile
and rotation, exact elevation grid, scripts, placed object graph, and required
PRO/FRM hashes through patch → critter → master overlay precedence. Script
execution and full campaign state remain absent; this contract is one input to
the bounded launcher-ready Hex slice, not a campaign-wide claim.

The next local-only compiler decodes only Map 126's admitted floor/roof tile
frames and placed-object frame/rotation pairs with the owned `color.pal`:

```powershell
python .\content\tools\prepare_fo2_temple_presentation.py `
  --profile "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\fallout2-profile.json" `
  --source-manifest "$env:LOCALAPPDATA\OpenNV\profiles\fallout2\temple-of-trials-v1.json" `
  --output-root "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1"
```

That disposable cache contains hash-bound PNGs and an asset-free provenance
manifest. It is local derived content and is never distributed. The runtime can validate the complete
cache/source/profile/recipe chain and construct Map 126 in Godot's 3D hex space:

```powershell
$Godot = '<path-to-Godot-4.7.2-Mono-console.exe>'
& $Godot --headless --path .\runtime -- `
  --fo2-temple-cache "$env:LOCALAPPDATA\OpenNV\cache\fallout2\temple-of-trials-v1\fo2-temple-presentation-cache.json" `
  --fo2-temple-build-proof `
  --report "$env:LOCALAPPDATA\OpenNV\proofs\fallout2\temple-runtime.json"
```

That proof builds the exact admitted floor and top-level object planes, derives
floor support and the central-hex blocker walk mask from owned MAP values, molds
the 45 source wall-object hexes into connected collision shells, and proves the
exact floor and wall colliders with headless physics rays. The non-source visual
shell is now suppressed while all 45 owned wall FRMs remain visible. A nonvisual cursor consumes
42 exact adjacent moves inside the 1,085-hex entry component, proving floor
contact and fail-closed boundary rejection. Multihex footprint semantics,
General Temple scripts, target AI/turns, broad combat/inventory, and
parity remain unimplemented. One deliberately bounded confrontation adapter
binds MAP critter serial 379/PID `01000003`/SID `04000001`, its owned Villager
PRO/MSG stats, and nested Spear serial 378/PID `00000007`. Adjacent player melee
uses visible derived HP/AP, deterministic damage, defeat-to-loot, inventory
visibility, and version-12 save/cold restore; it never executes `ARTemple.int` or
claims retail combat parity. A separate asset-free transition
contract proves that Map 126 has no door-prototype objects and binds its source
exit grids without executing `ARTemple.int`. The destination compiler independently binds
Map 3 `ARCAVES`, that exact incoming placement, 24 reciprocal exits to Map 126,
the 586-hex arrival component, and a 173-artifact disposable presentation cache.
Godot consumes that bounded Map 3 cache in a rendered 3D hex scene; ordinary
grounded movement now follows the exact 13-step path to exit serial 1738 and
loads ARTEMPLE Map 126 at tile 16486, elevation 0, rotation 0.
`content/tools/prepare_fo2_character_start.py` parses the exact Fallout 2
premades Narg, Mingan, and Chitsa from their GCD/BIO records and decodes the
owned picker, portraits, and male/female idle FRMs into a disposable local
cache. `scripts/Start-OpenNVFallout2Arroyo.ps1` opens that selector; keyboard or
mouse can Take a premade, Modify it, or Create a custom state. Modify/Create edit
name (1–11 characters), sex, age (16–35), and seven SPECIAL values (1–10 each,
exactly 40 total). Modify preserves the source premade's tags/traits unchanged;
Create leaves them explicitly unselected because their editing rules are not yet
transported. A single `LIVE 3D`/`PORTRAIT` button (or `V`) keeps each exact
owned Narg/Mingan/Chitsa panel available and switches the preview to the same
hash-bound owned FNV full-body donor used by the Map 3 gameplay actor. The
shared preview-set contract requires both sex variants, ordered body/left-hand/
right-hand roles, a verified presentation outfit, GLTF and sidecar hashes, and
an authored rigid attachment socket. Missing or incompatible input stops before
Godot; it never substitutes a procedural body, source-FRM player relief,
silhouette, or standee. The donor is presentation only—not classic geometry or
a parity claim—while exact GCD/FRM identity and gameplay state remain
authoritative. Modify/Create retains its bounded authored-state controls and
local portrait output; it has no substitute live 3D head. Confirm deterministically writes the matching 128×128 portrait under
the OpenNV user-data portrait directory; the hash-named PNG contains no owned
pixels. Confirm then hands the state and sex-correct FRM to the grounded Map 3
player at exact tile 28707. Its version-12 atomic OpenNV user-data save retains
the selected mode, owned GCD/BIO source basis, custom profile state, current map,
elevation, tile, facing, transform, bounded movement/presentation modes, and the
source exit transition identity plus the bounded Temple target HP/AP/combat and
Spear-loot state and an explicit source-panel/generated-portrait appearance
contract. Hair and skin editing remain absent; a fresh process validates the
portrait path, SHA-256, dimensions, generator, and face shape before restoring
the same player. The
launcher passes these exact five hash-matched local artifacts and the isolated
save path to the same character-start scene. This is bounded custom character
creation and Hex playability, not a complete campaign save, FPS/VR, or retail
parity. The opening Elder movie's natural end and Skip both present the exact
terminal source frame/fade, converge at black, prepare the same live Arroyo
camera, and execute the same reveal/control release. The handoff implementation
is source-timed, but its live presentation remains human-review/unaccepted. The
22 admitted Temple/Arroyo torch anchors use exact owned opaque FRM emitter
pixels and centroid joined to source MAP light placements. The admitted emitter
is a static frame; source flame animation and smoke are not claimed. The
[source audit](docs/evidence/fo2-map3-torch-animation-source-audit.md) records
why the three retained torch FRMs cannot lawfully advance and separates the
real elevation-2 12-frame firepit identity.
### Classic humanoid donor preflight and no-media launch

The following commands only validate existing local artifacts or launch the
existing bounded runtime. They do not build a cache, capture media, or claim
visual acceptance. Supply an explicitly produced owned install manifest and
already-prepared classic artifacts. Before runtime, the resolver hash-verifies
the install-manifest output, the referenced opening-manifest join, and the
standalone preview-set payload; it never discovers a donor by scanning a cache.

```powershell
$ClassicHumanoidInstallManifest = '<absolute-owned-install-manifest.json>'
$DonorPreviewSet = pwsh -NoProfile -File .\scripts\Resolve-ClassicHumanoidDonorPreviewSet.ps1 `
  -InstallManifest $ClassicHumanoidInstallManifest
pwsh -NoProfile -File .\scripts\Assert-ClassicHumanoidDonorPreviewSet.ps1 `
  -PreviewSet $DonorPreviewSet

python -m unittest `
  content.tests.test_classic_humanoid_launch_contract `
  content.tests.test_classic_humanoid_no_placeholder_runtime
```

```powershell
pwsh -NoProfile -File .\scripts\Test-GodotRuntime.ps1 `
  -Godot '<Godot-console.exe>' `
  -Fo1HexScene '<absolute-fo1-hex-scene.json>' `
  -ClassicHumanoidInstallManifest $ClassicHumanoidInstallManifest

pwsh -NoProfile -File .\scripts\Start-OpenNVFallout2Arroyo.ps1 `
  -ClassicHumanoidInstallManifest $ClassicHumanoidInstallManifest
```

## Character path is a real choice

Choose the path **before creating a character**. Each choice has its own
profile and save boundary.

| Path | Selector and shared-state rule | Current implementation boundary | JAM / edition rule |
| --- | --- | --- | --- |
| Fallout 1 | Normal/FPS retains the classic native picker. Hex uses the classic picker template with an optional owned-donor preview; VR reuses the normal/FPS creator and character state. | Selected premade/custom identity and sex cold-restore into the gameplay actor. Exact GCD/FRM data remains authoritative; all 3D humanoid presentation requires the shared owned FNV donor set and is non-parity. No procedural or FRM-player substitute is admitted. Native visual acceptance remains pending. | Separate from the Gamebryo-family profiles. |
| Fallout 2 | Normal/FPS must retain the classic native picker. Hex keeps the owned classic picker with an optional owned-donor preview; VR must reuse the normal/FPS creator and character state. | Only the bounded Hex route is enabled. Its version-12 save cold-restores the owned source basis, local portrait, Map 3/Temple transform, and the selected peaceful-ARVILLAG or Temple-combat branch state described in the [canonical ledger](docs/evidence/fo2-first-slice-branch-ledger.md). The 3D player/creator preview requires the shared owned FNV donor set and is non-parity; source MAP/PRO/FRM remains the environment and identity authority. The branches do not merge; tag/trait editing and campaign-wide state remain absent. | No JAM layer. |
| New Vegas | Normal/FPS uses the native Doc Mitchell creator. A future Hex route uses the classic Hex picker template plus Custom. VR shares the normal/FPS Doc creator and save state; it never gets a separate picker. | The standalone opening is still a bounded development route; Hex and fully validated VR character creation are not implemented. | Standalone Mojave save; JAM is optional only after its dependencies and portable semantics validate. |
| Fallout 3 | Normal/FPS uses the native Vault 101 creator. A future Hex route uses the classic Hex picker template plus Custom. VR shares the normal/FPS Vault creator and save state; it never gets a separate picker. | The standalone CG00/CG01 work remains a bounded development route; Hex and fully validated VR character creation are not implemented. | Standalone Capital Wasteland save; TTW is a distinct edition/path. |
| TTW | One Capital Wasteland-to-Mojave identity follows the Fallout 3 native creator contract; future VR shares it rather than creating another character. | The launcher presents separate TTW-FO3 and TTW-FNV editions. Only TTW-FO3 has the bounded records-only opening compiler/executor; TTW-FNV has no Doc/runtime route. Neither edition is playable. | A TTW save cannot adopt an existing standalone FO3 or FNV character. |

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
Godot, and owns an isolated Vault Dweller save. A valid save exposes **Continue**,
which restores the saved player hex, presentation identity, Pip-Boy/classic HUD,
and finite camera mode/yaw/pitch/zoom under black before controls; it never
replays the picker/movie or resets the entry. Older saves missing either required
identity or camera contract remain non-continuable. Only V13ENT is playable; the
other 95 inventoried maps, full dialogue/quest simulation, combat-formula parity,
and promoted OpenXR play are not connected. A simulator-only adapter reaches
the shared V13ENT state but remains launcher-disabled. This route now begins at a functional, asset-free
OpenNV Fallout-style menu before the owned character picker. Fallout 1's retail
startup logos and exact retail menu art/presentation are not implemented.

The New Vegas saloon/exterior component remains independently playable, not
only a renderer, and its saloon interior is now also the second linked target
of the default Doc route. The compact launcher now registers one explicit
immutable New Vegas cache root through **Set up New Vegas**; launching never
rebuilds or silently swaps that cache, and the legacy Godot cache is only a
fallback when no registration exists. Its hash-pinned retail baseline
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
latest bounded native checkpoint proves chair reference `001059b0` and its
exact furniture-marker formula at stage 8 with zero marker position/basis error.
The current compiler/runtime additionally binds the patient bed fail-closed to
`REFR 00103e5b`, base `FURN 00106a6a` / `NVbedtwin01`, and the owned model hash;
that newer identity correction still needs a fresh native/retail differential.
The owned cigarette `ANIO 00083519` is attached to `Bip01 R Hand`. Its admitted
NIF and smoking KF contain no particle/effect spawn contract, so visible puffs
are separately labelled as a first-party, tip-anchored non-parity adaptation,
not a transported source emitter. The cited Stage-36 run accepts a visible name
and exposes all 43 admitted CTL/EGM controls on its older owned default-male
head preview. Current compiler/runtime code instead admits distinct hash-bound,
full-body male and female artifacts for the exact default race/hair/eye
identities; those artifacts have not been regenerated or accepted natively, and
every other selection still fails closed to owned source-texture tiles. The same
earlier run applies the exact stage-40 exit/root handoff and reaches the stage-55
autosave. It did not perform cold resume or generate accepted media. The native
report is
`D:\Builds\OpenNV-fnv-headless-exact-creator-20260829-r1\checkpoint-marker-v3-scoped2-report.json`
(SHA-256
`894598ee8644cb2ac3869fd645c420882f9980c722d96277d46f1d09f35e645b`).
This bounded result is separate from the older completed-route evidence below.
The
completed cold-Continue path maps the saved movement, look, rollover-derived
activation, and fighting bits back onto `CellPlayer`, including the authored
disabled-combat state. Pip-Boy visibility is restored separately; saved POV and
sneaking bits still lack runtime consumers. The
bounded default cache joins the reciprocal Doc Mitchell house/exterior and
Goodsprings exterior/saloon pairs in one eagerly instantiated bounded composite.
From a completed stage-200 save, the owned Continue button signal restores the
Doc route. A fresh four-family cache and current runtime pass the first portal,
the exact animated Goodsprings gate, the source-backed Prospector Saloon porch,
and `0010636f` → `0010618e`. The saved saloon CELL and player transform then
cold-restore without replaying a transition. All three spaces remain instantiated, but
only the authoritative current CELL remains active for visibility, processing,
physics, and lights; linked CELLs stay preloaded and suspended. Remaining work includes reverse
traversal, dynamic time/weather, neighboring exterior-grid streaming, integrated-route
OpenXR, Sunny behavior, and an uninterrupted whole campaign. See the
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
Owned NIF dynamic convex and box collision now drives a separate shared pickup
hold/move/drop path: Z on desktop and right A/primary in OpenXR hold an item,
release drops it, while E/right grip retains the ordinary collect/activate path.
Movable pickup transforms and velocities cold-restore in the v7 campaign save;
assets without a unique owned dynamic body remain collectible but are honestly
reported as unsupported for physical grabbing. The owned-data Saloon slice also
contains an unsupported experimental practice pool table. The intact retail
table triangles, authored cue/rack/four placed balls, and NIF convex bodies load,
but the flat native gate currently fails before ball contact: the retail ball
references decorate the ruined table and do not make a playable layout on the
intact replacement. Pocket, reset, cold restore, OpenXR layout, full eight-ball
rules, and physical-headset acceptance are therefore not claimed.

### Reproducible development sneak peeks

`scripts/build_opennv_sneak_peek.py` builds a hash-bound current-development
reel from a private shot manifest. The versioned policy in
`content/recipes/opennv-sneak-peek-video-v1.json` emits 1080p and phone-vertical
H.264/AAC copies plus a report containing every source/output hash and media
probe. Owned screenshots, movies, the private manifest, intermediate segments,
and finished reels remain outside the repository. A successful edit is not a
retail-parity or full-campaign claim; those fields remain explicitly false in
the report.

## Release contents

Future runtime releases will contain the exported Godot runtime, direct content
contracts, launcher, and source-revision metadata. They will not contain:

- commercial game files, DLC, or conversion output;
- third-party mod archives or downloader credentials;
- a player's saves, profiles, or mod-manager state.

No playable Godot runtime is currently published. Historical preview archives
retain the notices that applied when they were built; see [NOTICE.md](NOTICE.md).
