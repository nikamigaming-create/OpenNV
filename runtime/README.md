# OpenNV Godot runtime

This is the first-party Open Nevada runtime. It uses Godot Forward+ and accepts
only artifacts produced by the direct retail-content pipeline in `../content`.

The current slice reads the owned master directly and runs a playable,
recipe-pinned Goodsprings sandbox. It loads 154 visible/held assets, 348
references, 266 textures, 339 materials, 97 pickups, five containers, 24
authored lights, and the authored enabled saloon settler.
The incoming XTEL owns the spawn. The `.357` pickup uses retail damage and clip
data; inventory, ammo, objective, removed pickups, and door state autosave and
cold-reload. One fully resolved crate is transferable; containers backed by
unimplemented leveled-list records stay explicitly locked. It does not claim
actors, damageable combat, simulated projectiles, or a complete campaign.

Run the complete repository gate from the repository root:

```powershell
pwsh -File scripts/Test-GodotRuntime.ps1
```

Pass `-FalloutNewVegasData` to make the gate validate the owned master and BSA,
extract the model directly, build a temporary cache, load it in Godot, and
delete the cache afterward. No retail-derived file or generated conversion
belongs in Git.

Actor parity captures require `--cell-scene`, an actor scene/set, `--capture-root`,
and the compact oracle artifact supplied as `--retail-state-contract`. The
runtime rejects a missing/mismatched ACHR, shot set, pose, geometry gate, or
projection label. A provisional retail FOV may improve a failing comparison but
cannot promote exact projection parity.
Cell scene v5 and actor scene v3 are required. Older caches carry a mirrored yaw,
positional material binding, incomplete shader state, or unhashed actor sidecar
and are rejected.

Build an asset-free experimental Windows archive after installing the pinned
Godot Mono export templates and `content/requirements-build.txt`:

```powershell
pwsh -File scripts/Build-GodotRuntime.ps1 -OutputRoot D:\Builds\OpenNV
```

The archive contains the Godot executable and a packaged legal-content helper,
but no commercial content. On first launch, select a legal Fallout: New Vegas
`Data` folder; OpenNV prepares its private cache and enters the playable saloon
sandbox. Python and OpenMW are not required on the player's machine. Later
launches reopen that verified cache automatically.

Use WASD and mouse-look, press E to pick up items, open containers, or operate a
door, left-click to fire an equipped weapon, and F5 to save. The HUD tracks the
four-stage sandbox objective and inventory. Packaging proves the route and a
separate cold reload before accepting the build.

The same sandbox has an experimental OpenXR mode. Choose **OpenXR mode** in the
launcher, or run `OpenNV.exe --xr-mode on -- --vr`. Oculus Touch and the OpenXR
1.1 generic-controller fallback are declared: left stick moves, right stick
snap-turns, right grip activates, right trigger fires with haptics, B reloads,
and X saves. VR starts with the owned master-record 10mm pistol profile equipped,
one full magazine, and one reserve magazine. The tracked eye is calibrated once
to 1.68 metres above the authored floor. The HUD is mounted in world space on the
left controller. A first Oculus hardware run exposed missing generic bindings
and floor-height calibration; this corrected path remains pending a clean
hardware rerun. This first path is Windows PCVR; a standalone Quest APK/export/
install gate is not implemented yet.

Add `-FalloutNewVegasData <path>` to the build command for a local end-to-end
gate of the exported executable, packaged helper, legal cache, and Godot load.

The private Fallout 1 tactical slice launches from a prepared, ignored owned
cache with:

```powershell
Godot_v4.7.2-stable_mono_win64.exe --xr-mode off --path runtime -- `
  --fo1-hex-scene <cache>\hex-scene.json --save-path <cache>\v13ent-hex-save.json
```

Its controls are MMB orbit/tilt, RMB drag-pan, wheel zoom toward the cursor,
WASD/arrows/edge pan, `F` player focus, `Home` entry-to-door route reset,
left-click path movement/target selection, `Tab` hostile cycle and auto-frame,
double-click or `X` attack, `G` grid, `V` source scenery, `B` experimental 3D
topology blockout, `Space` end turn/rat turn, and `F5` save. Static source
walls, rocks, and clutter are ground-anchored and world-locked at the authored
isometric angle; actors remain camera-facing for readability. Rats use a bright
source-art silhouette, hex beacon, and screen-space target reticle. The rough
procedural 3D blockout is deliberately hidden by default and is not an authored
asset replacement. This route uses the scripted first-run spawn, owned
player/rat art, source rat combat fields, and a bounded combat proof. It is not
a full Fallout campaign or retail combat simulation.
