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
later launches. The current recipe-pinned proof opens the Goodsprings Prospector
Saloon at the main entrance's data-defined XTEL target. Use WASD/mouse, E to
activate, left-click to fire the initially equipped owned-data 10mm, R to
reload, and F5 to save.
Aim at the intact pool table or one of its balls and press E to enter practice
mode. Left-click strikes along the camera heading, the mouse wheel changes the
configured power, R resets every ball to its authored Saloon transform, and E
or Escape returns to the weapon. In OpenXR, grip enters/exits, hold trigger and
sweep the tracked cue through the cue ball, and B resets the table.

The New Vegas launcher path is enabled only for this sandbox. Fallout 3, TTW,
JAM, and the full New Vegas campaign remain disabled until their own gates pass.
The launcher also exposes an experimental OpenXR toggle. Meta/Oculus Touch and
the OpenXR generic-controller fallback are declared. A repo-local simulator
passes two retail hands, both sticks, locomotion, snap turn, door/fire/reload/
save actions, and native stereo capture. Physical-headset final-eye validation
is still pending and the flat path remains the default.
