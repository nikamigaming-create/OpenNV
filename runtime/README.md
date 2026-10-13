# OpenNV Godot runtime

The runtime is C# on Godot 4.8-dev7 Mono and .NET 10. It accepts a selected legal installation,
reads its formats directly, and creates Godot world and presentation objects in
memory.

Build:

```powershell
. .\scripts\ManagedToolchain.ps1
Initialize-OpenNVManagedToolchain
dotnet build .\runtime\OpenNV.sln -c Release
```

Run the full source gate:

```powershell
$godot = .\scripts\Install-GodotToolchain.ps1
.\scripts\Test-GodotRuntime.ps1 -Godot $godot
```

`godot-toolchain.json` pins the official archive, hash and matching managed SDK.
The launcher, gate and owned offline audit validate the actual selected engine.
The new project texture-streaming setting is enabled, but direct owned DDS
uploads remain resident. Live diagnostics report actual total/streamed texture bytes;
an enabled setting is not evidence of streamed owned textures.

The Godot runtime is the product launcher and the game. Ordinary startup uses
`--launcher` with no selected world; choosing a profile then transitions into
the selected route through the same `RuntimeCoordinator` and process. Direct
diagnostic launch requires a live installation root, campaign identity, and
save path.
