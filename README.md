# Open Nevada

**Open Nevada** (OpenNV) is an independent, cross-platform launcher and
compatibility project for worlds built from game assets that a player legally
owns. Its interface, product identity, and launcher contract are Open Nevada's
own; it is not a front end branded as or affiliated with another engine or
game series.

The public home is **[opennevada.com](https://opennevada.com)**. The shorter
**[opennv.org](https://opennv.org)** is reserved for technical and community
use; until DNS hosting is deployed, GitHub remains the release source.

![Open Nevada atlas visual](desktop/assets/open-nevada-atlas-hero-v1.png)

The first-party desktop launcher has a portable Electron shell for Windows,
macOS, and Linux. Runtime builds are promoted per platform only after the same
campaign and compatibility tests pass; the current downloadable runtime
preview is Windows x64. See [the launcher architecture](docs/desktop-launcher.md).
See [the domain deployment plan](docs/domains.md) and [Cloudflare Pages
handoff](docs/cloudflare-pages.md) before publishing the public site or a
redirect.

Open Nevada ships no commercial game assets, DLC, conversion output, or
third-party mod archives. Players provide those from lawful sources.

## Character path is a real choice

Choose the path **before creating a character**. Each choice has its own
profile and save boundary.

| Path | Character | JAM rule |
| --- | --- | --- |
| New Vegas | Separate standalone Mojave character | Start base and add JAM later, or begin with JAM. Keep JAM enabled after a save uses it. |
| Fallout 3 | Separate standalone Capital Wasteland character | Vanilla standalone route. Choose TTW at character creation if the character should continue into the combined world. |
| TTW | One Capital Wasteland-to-Mojave character | A separate combined-world path, base or JAM. It cannot be retrofitted onto an existing standalone save. |

This makes the important distinction visible rather than hiding it in mod
files: JAM is modular; TTW is a character-path decision.

## Mod support without a Windows-only ceiling

Open Nevada accepts content and mod sources through isolated profiles rather
than touching a game installation. A Windows-only native plugin is not a
product-level exclusion. It enters a compatibility pipeline:

1. record the extension behavior and its needed events/commands;
2. implement a portable OpenNV semantic contract in the runtime;
3. run the real mod through a recorded launch validation;
4. promote it to *supported* only when that behavior is reproducible.

That is how major extender-dependent mods can work across platforms without
pretending that an arbitrary Windows DLL is safe to load into a different
runtime. The current catalog distinguishes validated modules from ones still
waiting on an extender bridge. See [the mod policy](docs/mods.md).

## Quick start: current Windows runtime preview

1. Download and extract `OpenNV-nightly-windows-x64.zip` outside `Program Files`.
2. Install the games you own from a legal store and point OpenNV at their `Data`
   folders:

   ```powershell
   .\scripts\Configure-OpenNV.ps1 `
     -Fallout3Data 'D:\SteamLibrary\steamapps\common\Fallout 3 goty\Data' `
     -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
   ```

3. Review the character choices, then launch one:

   ```powershell
   .\scripts\Start-OpenNV.ps1 -ShowChoices
   .\scripts\Start-OpenNV.ps1 -Campaign NewVegas
   ```

For TTW, run its official installer into a dedicated output directory and
register that directory. OpenNV never writes to a game directory or to the
official conversion output. See [installation](docs/installation.md) and
[nightlies](docs/nightlies.md).

## Release contents

Every runtime release contains the OpenNV runtime, launcher bridge, module
catalog, and source-revision metadata. It does **not** contain:

- commercial game files, DLC, or conversion output;
- third-party mod archives or downloader credentials;
- a player's saves, profiles, or mod-manager state.

The runtime includes code subject to its upstream free-software license.
Required notices, source information, and contributor attribution are retained
in every archive; see [NOTICE.md](NOTICE.md).
