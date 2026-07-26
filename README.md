# OpenNV

OpenNV is a Windows compatibility runtime and headless launcher for legally
installed **Fallout 3**, **Fallout: New Vegas**, and **Tale of Two Wastelands**
content. It ships no Bethesda game assets, DLC, TTW output, or third-party
mods.

The project has three campaign choices at character creation:

| Campaign | Character | JAM |
| --- | --- | --- |
| New Vegas | Standalone Mojave character | Optional, can be added later |
| Fallout 3 | Standalone Capital Wasteland character | Vanilla only |
| TTW | One Capital Wasteland-to-Mojave character | Optional, can be added later |

Download the current Windows preview from the repository's
[Releases](../../releases). Nightlies are deliberately labelled as previews:
every promoted game feature or mod module must have a recorded compatibility
test rather than merely load an `.esp`.

## Quick start

1. Download and extract `OpenNV-nightly-windows-x64.zip` outside `Program Files`.
2. Install Fallout 3 and/or Fallout: New Vegas from a legal store and point
   OpenNV at each game's `Data` folder:

   ```powershell
   .\scripts\Configure-OpenNV.ps1 `
     -Fallout3Data 'D:\SteamLibrary\steamapps\common\Fallout 3 goty\Data' `
     -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
   ```

3. Review the character-creation choices, then launch one:

   ```powershell
   .\scripts\Start-OpenNV.ps1 -ShowChoices
   .\scripts\Start-OpenNV.ps1 -Campaign NewVegas
   ```

For TTW, run the official TTW installer first and register its **separate
output directory**. OpenNV never writes to a game directory or the TTW output.
See [installation](docs/installation.md), [nightly policy](docs/nightlies.md),
and [mod policy](docs/mods.md).

## Release contents

Every release contains the OpenNV runtime, launcher scripts, a module catalog,
and source-revision metadata. It does **not** contain:

- Fallout 3, Fallout: New Vegas, DLC, or TTW assets;
- Nexus/Mod Organizer downloads or third-party mod archives;
- a user's saves, configuration, credentials, or mod-manager state.

The runtime is an OpenMW-derived build; release metadata records the exact
source revisions and upstream licensing obligations.
