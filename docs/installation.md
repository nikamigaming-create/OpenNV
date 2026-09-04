# Installation and development

Install .NET 9, Godot 4.7.2 Mono, Node.js 22, and PowerShell 7.

Verify the checkout:

```powershell
dotnet build .\runtime\OpenNV.sln -c Release
npm ci --prefix .\desktop
npm test --prefix .\desktop
.\scripts\Test-GodotRuntime.ps1 -Godot 'C:\Path\To\Godot_console.exe'
```

Start the desktop launcher:

```powershell
.\scripts\Start-OpenNV.ps1
```

Select a legally owned installation in the launcher. The installation remains
read-only. Saves and launcher preferences are written under the user's OpenNV
application-data directory.
