# Install Open Nevada without touching game folders

The desktop launcher code is portable. The currently published runtime preview
is Windows x64, so these setup commands use PowerShell; macOS and Linux runtime
packages will use the same profile and launcher contract when they pass their
platform promotion gates.

Extract the runtime to a normal writable folder, for example
`D:\Games\OpenNV`. Do not place it in a game directory and do not copy its
files into `Data`.

Open Nevada uses licensed source folders and generates profiles below its own
`profiles/` directory.

## Register legal game assets

Register only folders you own. The setup command validates supplied
directories, records them in `local/paths.json`, and never modifies them.

```powershell
.\scripts\Configure-OpenNV.ps1 `
  -Fallout3Data 'D:\SteamLibrary\steamapps\common\Fallout 3 goty\Data' `
  -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
```

Standalone Fallout 3 and New Vegas automatically mount only DLC the player
actually owns. A base-game owner can use a vanilla route without pretending to
own DLC.

## Register TTW

TTW requires its full official Fallout 3 and New Vegas DLC/preorder set. Run
the official installer into a dedicated empty mod/output directory, then
register that directory:

```powershell
.\scripts\Configure-OpenNV.ps1 `
  -TtwRoot 'D:\Modlists\fnv\mods\Tale of Two Wastelands - OpenMW'
```

Do not point TTW at either game's `Data` directory. Open Nevada combines the
licensed game data with the immutable official TTW output through a generated
profile.

## Choose a character path

```powershell
.\scripts\Start-OpenNV.ps1 -ShowChoices
```

Choose New Vegas, standalone Fallout 3, or TTW before creating a character.
TTW is a separate Capital Wasteland-to-Mojave path; it does not merge existing
standalone saves. JAM can be added later to New Vegas or TTW, but a save made
with JAM must keep JAM enabled.
