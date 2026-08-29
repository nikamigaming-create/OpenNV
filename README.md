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
| Fallout 2 Hex | Registered-profile launcher route into the original owned 640×480 Narg/Mingan/Chitsa selector plus source-backed Modify/Create for name, sex, age, and exact SPECIAL. Each premade keeps its distinct exact owned panel and one small Portrait/Live 3D toggle presents that panel on a curved live 3D surface. Take enters Map 3 and exact exit serial 1738 reaches ARTEMPLE Map 126. A bounded source-identified Temple Villager exposes player HP/AP, deterministic adjacent melee, defeat, exact nested Spear loot, visible inventory state, version-5 save, and cold restore | The Live 3D mode is an owned-reference panel relief, not rebuilt head geometry. Custom face/hair/skin controls and generated portraits are not implemented; v5 records that state explicitly. The Temple adapter does not execute target AI/turns, INT/dialogue, retail hit formulas, or general inventory. Full campaign, parity, FPS, and OpenXR remain absent |
| New Vegas | Owned menu, skippable intro, source-ordered Doc Mitchell speech/quest beats, stage-36 character selection, source-bound HUD/STATS/ITEMS/DATA contracts and Pip-Boy shell, and one ordered Doc house → Goodsprings exterior → saloon composite with both reciprocal XTEL pairs and normally enabled Sunny. Level 1 HP 200/200, AP 80/80, and XP 0/200 are source/default-derived, save-v6 persistent, and exposed to the Pip-Boy. The admitted four-family cache supports controller-door articulation, target-local static convex collision, current-CELL-only render/collision activation with linked CELLs preloaded, current-CELL WorldEnvironment switching, the owned clear-day atmosphere/cloud pair, one-time source-collision actor grounding, and a manifest-validated configured-input first-run/cold-Continue pair through the saloon door | The exact startup player-root/camera transform, complete saloon population/package AI, exterior surface/directional lighting, player deposits, Pip-Boy tab navigation, dynamic clock/weather/global state, reverse traversal, neighboring exterior-grid streaming, complete Gamebryo tile behavior, retail parity, integrated OpenXR, Hex, physical-headset acceptance, and the uninterrupted campaign remain unproven; the [Goodsprings actor/package audit](docs/evidence/fnv-goodsprings-actor-package-contract.md) records the next source contract |
| Fallout 3 | Owned main menu, intro, sex/name/appearance selection, and normal persistent CG00 progression into the owned Vault 101 birth room through stage 100. The bounded route plays the stage-65 Dad cue, applies stage-80/85 and stage-90 INFO `0001f379`, then executes seven of eight exact stage-100 commands through `SetPCYoung 1`; cold Continue restores without replaying one-shot cue/effects. A fresh pinned-Theora profile now compiles both movies plus the exact CG01 stage-0/stage-5 tree and the sex-specific two-line Dad-speech → stage-10 contract | The stage-10 runtime/save adapter is implemented but not native-proven: the fresh run correctly stops on stale absolute actor-derivative paths in the existing Vault 101 presentation cache. No freely playable first-person Vault 101 route, general package/dialogue interpreter, lip playback, Mom/player presentation, Hex, VR, or retail parity exists |
| TTW | Strict local profile/effective-source registration plus a launcher-validated, source-bound Fallout 3 CG00→CG01-stage-5 command/movie contract with dedicated TTW cache and save identities | No TTW command interpreter, Vault 101 world presentation, save runtime, xNVSE execution, or JAM runtime exists; every TTW presentation remains disabled |
| JAM | Dependency/profile inspection plus bounded JVS sprint and JBT time-dilation semantics | The full dependency and portable-semantic gates are incomplete, so JAM remains disabled |

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
`-Godot`. The compact launcher always shows Fallout 1, Fallout 2, New Vegas,
and Fallout 3 as its four top-level choices. Selecting a card exposes one
shared FPS / Hex / VR mode row and Play action below it, with unfinished modes
visible but disabled. Select **New
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
Vault 101 resource graph. The bounded development frontend can boot the CG00 sex/name flow,
resume its stage-60 character, select from source-backed playable race and
sex-aware hair/eye records, and persist the owned FaceGen defaults at stage 62.
The preview is an exact owned-texture inspection surface, not a 3D face render.
The exact `CG00PlayerSection4`, stage-65 parent appearance, stage-80
package/variable/reference, stage-85 zero-command result, and stage-90
four-command result compile and validate. The ordinary bounded flow renders the
owned Vault 101 birth room with grounded Doctor Li and direct `CG00Dad`
ACHR/NPC/race/FaceGen/outfit identity, plays the exact owned Dad cue/subtitle,
applies INFO `0001f379`, sets timer `2.2` and `runTimer=1`, presents the owned
white fade and sound, then executes seven of eight exact stage-100 commands
through `SetPCYoung 1`. Cold Continue restores stage 100 without replaying those
one-shot effects. The owned-profile producer also compiles the exact CG01
stage-0 command tree through its synchronous stage-5 result and the sex-specific
two-line Dad-speech transition into stage 10. The bounded runtime/save adapter
for that transition is implemented, but its fresh native proof currently stops
on stale absolute actor-derivative paths in the existing Vault 101 presentation
cache. This remains a deterministic bounded room/front-end
route, not retail camera, lighting, material, animation, lip, package, timing,
or freely playable first-person parity; the launcher therefore keeps every
Fallout 3 presentation disabled. General package/KF execution, a portable fresh
Vault 101 presentation cache, the eighth stage-100 command, Mom/player presentation, and the remaining opening
interpreter are active work. TTW and JAM
registration are documented in [the mod policy](docs/mods.md);
registration alone does not make either route runtime-playable.

The owned JAM 4.6 plugin currently transports two narrow desktop behaviors:
Shift-held forward sprint at its authored 1.75 multiplier and X-toggled Bullet
Time at its authored 0.5 world-time multiplier. The five missing local native
prerequisites and the unimplemented AP, UI, animation, audio, event, and cosave
semantics keep the launcher JAM route disabled; OpenNV never loads those DLLs.

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
General Temple scripts, target AI, dialogue, complete combat/inventory, and
parity remain unimplemented. One deliberately bounded confrontation adapter
binds MAP critter serial 379/PID `01000003`/SID `04000001`, its owned Villager
PRO/MSG stats, and nested Spear serial 378/PID `00000007`. Adjacent player melee
uses visible derived HP/AP, deterministic damage, defeat-to-loot, inventory
visibility, and version-6 save/cold restore; it never executes `ARTemple.int` or
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
transported. A single `LIVE 3D`/`PORTRAIT` button (or `V`) switches each exact
owned Narg/Mingan/Chitsa panel to a gently curved live 3D surface while the
original picker remains intact. This is an owned-reference relief, not rebuilt
head geometry. Modify/Create now adds one explicitly OpenNV-local face-shape
control (`ROUND`, `OVAL`, or `ANGULAR`) with a live procedural classic-green
preview. Confirm deterministically writes the matching 128×128 portrait under
the OpenNV user-data portrait directory; the hash-named PNG contains no owned
pixels. Confirm then hands the state and sex-correct FRM to the grounded Map 3
player at exact tile 28707. Its version-6 atomic OpenNV user-data save retains
the selected mode, owned GCD/BIO source basis, custom profile state, current map,
elevation, tile, facing, transform, bounded movement/presentation modes, and the
source exit transition identity plus the bounded Temple target HP/AP/combat and
Spear-loot state and an explicit source-panel/generated-portrait appearance
contract. Hair and skin editing remain absent; a fresh process validates the
portrait path, SHA-256, dimensions, generator, and face shape before restoring
the same player. The
launcher passes these exact five hash-matched local artifacts and the isolated
save path to the same character-start scene. This is bounded custom character
creation and Hex playability, not a complete campaign save, FPS/VR, or retail parity.
`scripts/Test-OpenNVFallout2CustomCharacters.ps1` captures a modified male and
created female path and cold-restores each in a separate process; its owned
screenshots and saves remain private under `%LOCALAPPDATA%\OpenNV\proofs`.

## Character path is a real choice

Choose the path **before creating a character**. Each choice has its own
profile and save boundary.

| Path | Selector and shared-state rule | Current implementation boundary | JAM / edition rule |
| --- | --- | --- | --- |
| Fallout 1 | Normal/FPS retains the classic native picker. Hex uses the classic picker template with only the small Portrait/Live 3D toggle and local Custom-face extension. VR reuses the normal/FPS creator and character state; it never gets a separate picker. | FPS and Hex are launcher-visible development presentations; VR remains pending and the campaign is not claimed complete. | Separate from the Gamebryo-family profiles. |
| Fallout 2 | Normal/FPS must retain the classic native picker. Hex keeps the owned classic picker with only Portrait/Live 3D and the local Custom-face extension. VR must reuse the normal/FPS creator and character state; it never gets a separate picker. | Only the bounded Hex route is enabled. Its version-6 save cold-restores the owned source basis, local portrait, Map 3/Temple transform, and Villager/Spear state; tag/trait editing and campaign-wide state remain absent. | No JAM layer. |
| New Vegas | Normal/FPS uses the native Doc Mitchell creator. A future Hex route uses the classic Hex picker template plus Custom. VR shares the normal/FPS Doc creator and save state; it never gets a separate picker. | The standalone opening is still a bounded development route; Hex and fully validated VR character creation are not implemented. | Standalone Mojave save; JAM is optional only after its dependencies and portable semantics validate. |
| Fallout 3 | Normal/FPS uses the native Vault 101 creator. A future Hex route uses the classic Hex picker template plus Custom. VR shares the normal/FPS Vault creator and save state; it never gets a separate picker. | The standalone CG00/CG01 work remains a bounded development route; Hex and fully validated VR character creation are not implemented. | Standalone Capital Wasteland save; TTW is a distinct edition/path. |
| TTW | One Capital Wasteland-to-Mojave identity follows the Fallout 3 native creator contract; future VR shares it rather than creating another character. | Runtime support remains bounded and cannot claim the combined campaign complete. | A TTW save cannot adopt an existing standalone FO3 or FNV character. |

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
The owned-data Saloon slice also includes an experimental practice pool table:
the intact retail table triangles, authored cue/rack/four placed balls, NIF
convex bodies, shared flat/OpenXR strike simulation, and v2 save state are
software-gated. Full eight-ball rules and physical headset acceptance are not
claimed.

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
