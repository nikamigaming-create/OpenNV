# Install OpenNV without touching game folders

Extract the release to a normal writable folder, for example
`D:\Games\OpenNV`. Do not place it in a Bethesda game directory and do not copy
its files into `Data`.

OpenNV uses only licensed source folders and generates its profiles below its
own `profiles/` directory.

## Register legal game assets

Register only folders you own. The setup command validates that supplied
directories exist, records them in `local/paths.json`, and never modifies them.

```powershell
.\scripts\Configure-OpenNV.ps1 `
  -Fallout3Data 'D:\SteamLibrary\steamapps\common\Fallout 3 goty\Data' `
  -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
```

Standalone Fallout 3 and New Vegas automatically mount only complete owned DLC
sets. A base-game owner can therefore use the corresponding vanilla campaign
without pretending to own DLC.

## Register TTW

TTW itself requires the full official Fallout 3 and New Vegas DLC/preorder
set. Run the official installer into a dedicated empty mod/output directory,
then register that directory:

```powershell
.\scripts\Configure-OpenNV.ps1 `
  -TtwRoot 'D:\Modlists\fnv\mods\Tale of Two Wastelands - OpenMW'
```

Do not point TTW at either game's `Data` directory. OpenNV combines the
licensed game data with the immutable official TTW output through a generated
profile.

## Choose a character path

```powershell
.\scripts\Start-OpenNV.ps1 -ShowChoices
```

Choose a campaign before creating a character. New Vegas, standalone Fallout
3, and TTW saves do not cross over. JAM can be added later to New Vegas or TTW;
once a save has been made with JAM, keep launching it with JAM enabled.
