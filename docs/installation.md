# Installation and development

Install .NET 9, Godot 4.7.2 Mono, and PowerShell 7. Node.js 22 is only needed
for the retained JavaScript compatibility tests.

Verify the checkout:

```powershell
dotnet build .\runtime\OpenNV.sln -c Release
npm ci --prefix .\desktop
npm test --prefix .\desktop
.\scripts\Test-GodotRuntime.ps1 -Godot 'C:\Path\To\Godot_console.exe'
```

Start the Godot launcher/game:

```powershell
.\scripts\Start-OpenNV.ps1
```

Select a legally owned installation in the launcher. The installation remains
read-only. The selected world is entered by the same Godot process; no second
game executable is started. Saves and launcher preferences are written under
the user's OpenNV application-data directory.
