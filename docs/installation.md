# Installation status

OpenNV is replacing its historical runtime preview with a first-party Godot
runtime. No playable Godot package is published yet, and archived previews are
not the current engine.

Developers can validate the asset-free synthetic slice on Windows with Godot
4.7.1 Mono, .NET 9, and Python 3.11:

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

For a persistent local developer cache, run
`Configure-OpenNVRuntime.ps1 -FalloutNewVegasData <path>`. The input is
read-only. No commercial file or conversion output is committed or packaged.

Player installation instructions will return only after the Godot runtime
passes campaign, natural-route, persistence, packaging, and retail differential
gates.
