# Installation status

OpenNV is replacing its historical runtime preview with a first-party Godot
runtime. The checked-in Windows slice is a playable experimental Goodsprings
sandbox; no full-campaign package is published yet.

Developers can validate the asset-free synthetic slice on Windows with Godot
4.7.2 Mono, .NET 9, and Python 3.11.9:

```powershell
python -m pip install -r content/requirements.txt
.\scripts\Test-GodotRuntime.ps1
```

To start the source launcher against the checked-in runtime on this computer,
install the launcher's dependencies once with `npm install` from `desktop`, then
run:

```powershell
.\scripts\Start-OpenNV.ps1
```

The script finds the local Godot 4.7.2 Mono build, sets
`OPENNV_RUNTIME_ROOT` to the repository runtime, and sets `OPENNV_GODOT` before
starting Electron. On another checkout, supply Godot explicitly:

```powershell
.\scripts\Start-OpenNV.ps1 -Godot 'C:\Path\To\Godot_v4.7.2-stable_mono_win64_console.exe'
```

This start command neither reads retail data nor creates a content cache. Game
inputs remain selected or registered inside the launcher.

An optional owned-data check starts from the player's legal game installation.
It hashes the master and meshes archive, extracts the model directly, prepares a
temporary cache, loads it in Godot, and removes the cache afterward:

```powershell
.\scripts\Test-GodotRuntime.ps1 `
  -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
```

Experimental packaged builds include `OpenNV.Content.exe` beside the Godot
runtime. A player launches `OpenNV.exe`, selects either their legal Fallout: New
Vegas installation folder or its `Data` folder, and the runtime prepares its
private cache directly. The player
does not install Python or another engine. The retail input is read-only, and no
commercial file or conversion output is committed or distributed. The verified
cache remembers the selected installation and is reopened automatically on
later launches. The separate saloon diagnostic opens the Goodsprings Prospector
Saloon at the main entrance's data-defined XTEL target. In that component, use WASD/mouse, E to
activate, left-click to fire the initially equipped owned-data 10mm, R to
reload, and F5 to save.
Aim at the intact pool table or one of its balls and press E to enter practice
mode. Left-click strikes along the camera heading, the mouse wheel changes the
configured power, R resets every ball to its authored Saloon transform, and E
or Escape returns to the weapon. In OpenXR, grip enters/exits, hold trigger and
sweep the tracked cue through the cue ball, and B resets the table.

The launcher shows four top-level game choices: Fallout 1, Fallout 2, New Vegas,
and Fallout 3. Fallout 1 enables registered Hex/FPS while VR stays disabled;
Fallout 2 visibly lists disabled Hex/FPS/VR choices; New Vegas enables original
flat and experimental OpenXR while JAM stays disabled; and Fallout 3 keeps
FPS/Hex/VR disabled while its bounded menu/CG00 development frontend remains
non-playable. TTW is an edition, not a fifth game button. On
this development machine Fallout 1's generated V13ENT
inputs and Fallout 3's owned GOTY profile are registered. New Vegas launches the
owned menu, skippable intro, and Doc Mitchell route from the verified local
cache. The launcher supplies a profile-owned Courier save path; the runtime's
`user://saves/new-vegas-opening-v1.json` is only the direct-launch fallback.
That campaign save remains separate from the older Goodsprings sandbox save. After the opening completes,
its source-bound HUD/Pip-Boy runtime shell
reads the same inventory, quest, map, and save state; complete tile interaction
and retail UI parity remain pending. STATS currently shares the verified ITEMS
frame while its remaining Gamebryo layout expressions are unsupported. The
default cache preloads the ordered Doc house, Goodsprings active exterior set,
and saloon composite with both reciprocal XTEL pairs. From a completed stage-200
save, configured flat input traverses both forward XTEL links; campaign save v5
records saloon CELL `00106185`, and a fresh owned-menu Continue restores the
unchanged save and player transform there. Reverse traversal, neighboring CELL
streaming, and integrated-route OpenXR acceptance remain pending. The registered
Fallout 3 development frontend opens its
profile-backed menu, plays a locally
converted and hash-verified copy of the owned intro, and converges through
Escape or the Skip button on CG00 sex/name selection, a persistent stage-60
character, and source-backed race/hair/eye selection persisted at stage 62.
Exact Section 4 and stage-65/80/85 contracts compile and validate, but normal
progression stops at stage 62 until their authored package/dialogue triggers and
Vault 101 world execute; it does not persist a synthetic later quest state.
The current preview shows verified owned source textures rather than a 3D
FaceGen actor. Fallout 1 OpenXR has a shared-state V13ENT simulator adapter that
passes locomotion, snap turn, fire, reload, and save. XR door use,
campaign-native hands/weapon/UI, launcher enablement, and physical-headset
acceptance remain unpromoted. TTW, complete JAM runtime/launcher support, Fallout 3
`CG00PlayerSection4` package execution and Vault 101 world play, and all complete
campaigns also remain unpromoted.

The legally owned Fallout 2 install can be registered without producing a
content cache:

```powershell
.\scripts\Register-OpenNVFallout2.ps1 `
  -Fallout2Root 'D:\SteamLibrary\steamapps\common\Fallout 2'
```

This validates and hashes `master.dat`, `critter.dat`, and `patch000.dat`, plus
their DAT2 directory identities. It does not copy any member. The registered
profile can be passed to `content/tools/fo2_first_slice.py` to emit an
asset-free, hash-bound Temple MAP/PRO/FRM source manifest. Registration and
source transport do not make Fallout 2 playable: character creation, scripts,
gameplay/save state, and launcher-ready presentations remain unimplemented.

`content/tools/prepare_fo2_temple_presentation.py` can then decode only the
source manifest's admitted tile and object frames into a disposable local PNG
cache. The cache includes a provenance manifest, remains derived owned content,
must not be distributed, and does not change runtime readiness.
The bounded runtime can verify that cache, construct the exact admitted Map 126
floor/object scene, and prove its source-derived floor/wall colliders with
physics rays. The proof remains headless and does not establish rendered parity,
player interaction, or playability.

The registered Fallout 1 route now opens an asset-free original-style menu;
**New Game** enters the owned character picker and skippable owned Overseer
movie before releasing the selected Hex/FPS view in V13ENT. Its original retail
startup logos and exact retail main-menu presentation are not implemented.
The launcher also shows a diagnostic OpenXR choice but keeps it disabled.
Meta/Oculus Touch and
the OpenXR generic-controller fallback are declared. A repo-local simulator
passes two retail hands, both sticks, locomotion, snap turn, door/fire/reload/
save actions, and native stereo capture. Physical-headset final-eye validation
is still pending and the flat path remains the default.
