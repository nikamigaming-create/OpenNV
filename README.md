# Open Nevada

**Open Nevada** (OpenNV) is an independent Godot-based runtime, direct retail
content pipeline, and cross-platform launcher for worlds built from game assets
that a player legally owns. Its engine, interface, product identity, and
launcher contract are Open Nevada's own.

> **Live:** **[opennevada.com](https://opennevada.com)** is the public Open
> Nevada home. **[opennv.org](https://opennv.org)** is the short technical and
> community address, permanently forwarding to the canonical site.

Playable runtime downloads are paused while the new first-party Godot runtime
passes its promotion gates. GitHub remains the source and future release
authority; archived previews are not the current runtime.

![Open Nevada atlas visual](desktop/assets/open-nevada-atlas-hero-v1.png)

The first-party desktop launcher has a portable Electron shell for Windows,
macOS, and Linux. It now reads the in-repository Godot runtime manifest directly
instead of using a Windows-only engine bridge. Runtime builds are promoted per
platform only after the same campaign and compatibility tests pass. See [the
launcher architecture](docs/desktop-launcher.md).
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

## Current Godot development slice

The current checked-in slice keeps the synthetic NIF gate and adds one
hash-pinned, data-driven retail interior. It directly resolves the Goodsprings
Prospector Saloon CELL, its REFR-to-base relationships, 117 unique rendered
assets, 251 yaw-safe placements, 194 textures, 274 material bindings, 24 placed
lights, and the incoming XTEL spawn. Godot loads the dense cell, generates
collision, and proves that the spawn hits the floor and a physical ray passes
after the authored entry door opens.

CI pins the official Godot 4.7.1 Mono Windows archive by SHA-256
`764a089809fb1a6f745686ce9f6d3ca83adce8fb60fb9a4e2324b63baaebaa45`.

```powershell
python -m pip install -r content/requirements-build.txt
.\scripts\Test-GodotRuntime.ps1
.\scripts\Test-GodotRuntime.ps1 `
  -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
```

`Build-GodotRuntime.ps1` packages the legal-content helper with the experimental
Windows export. The resulting `OpenNV.exe` lets a player select their owned
`Data` folder directly; it does not require Python or another engine at runtime.

This is a real cell/runtime path, not a playable campaign. See the
[single-page architecture](docs/architecture.md),
[installation status](docs/installation.md), [clean implementation boundary](docs/clean-room.md),
and [release policy](docs/nightlies.md).

## Release contents

Future runtime releases will contain the exported Godot runtime, direct content
contracts, launcher, and source-revision metadata. They will not contain:

- commercial game files, DLC, or conversion output;
- third-party mod archives or downloader credentials;
- a player's saves, profiles, or mod-manager state.

No playable Godot runtime is currently published. Historical preview archives
retain the notices that applied when they were built; see [NOTICE.md](NOTICE.md).
