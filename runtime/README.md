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

Actor parity captures require `--cell-scene`, an actor scene/set, `--capture-root`,
and the compact oracle artifact supplied as `--retail-state-contract`. The
runtime rejects a missing/mismatched ACHR, shot set, pose, geometry gate, or
projection label. A provisional retail FOV may improve a failing comparison but
cannot promote exact projection parity.
Cell scene v10 and actor scene v5 are required. Older caches omit full authored
rotation/scale, configuration identity, deterministic outfit resolution,
current actor sidecars, or the owned-data first-person rig and are rejected.

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
door, left-click to fire, R to reload, and F5 to save. Flat and XR both start
with the owned-data 10mm equipped. The main door opens
both reciprocal references and can be crossed without a loading screen. The HUD tracks the
four-stage sandbox objective and inventory. Packaging proves the route and a
separate cold reload before accepting the build.

The same sandbox has an experimental OpenXR mode. Choose **OpenXR mode** in the
launcher, or run `OpenNV.exe --xr-mode on -- --vr`. Oculus Touch and the OpenXR
1.1 generic-controller fallback are declared: left stick moves, right stick
snap-turns, right grip activates, right trigger fires with haptics, B reloads,
and X saves. VR starts with the owned master-record 10mm pistol profile equipped,
one full magazine, and one reserve magazine. The tracked eye is calibrated once
to 1.68 metres above the authored floor. The HUD is mounted in world space on the
left hand. Legal `lefthand1st.nif` and `righthand1st.nif` assets provide the two
visible skinned hands; grip poses own their transforms and aim poses own rays.
The repo-local simulator passes tracking, both sticks, locomotion, snap turn,
door activation, fire, reload, save, supported eye height, and native stereo
capture. This corrected path remains pending a physical-headset rerun. This
first path is Windows PCVR; a standalone Quest APK/export/install gate is not
implemented yet.

Add `-FalloutNewVegasData <path>` to the build command for a local end-to-end
gate of the exported executable, packaged helper, legal cache, and Godot load.
