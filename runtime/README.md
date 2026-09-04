# OpenNV Godot runtime

The runtime is C# on Godot 4.7.2 Mono. It accepts a selected legal installation,
reads its formats directly, and creates Godot world and presentation objects in
memory.

Build:

```powershell
dotnet build .\runtime\OpenNV.sln -c Release
```

Run the full source gate:

```powershell
.\scripts\Test-GodotRuntime.ps1 -Godot 'C:\Path\To\Godot_console.exe'
```

Ordinary runtime launch is owned by the desktop launcher and requires a live
installation root, campaign identity, and save path.
