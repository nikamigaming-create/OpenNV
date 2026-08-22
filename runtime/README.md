# OpenNV Godot runtime

This is the first-party Open Nevada runtime. It uses Godot Forward+ and accepts
only artifacts produced by the direct retail-content pipeline in `../content`.

The current slice reads the owned master directly and runs a playable,
recipe-pinned Goodsprings sandbox. It loads 153 assets, 348 references, 255
textures, 332 materials, 97 pickups, five containers, and 24 authored lights.
The incoming XTEL owns the spawn. The `.357` pickup uses retail damage and clip
data; inventory, ammo, objective, removed pickups, and door state autosave and
cold-reload. One fully resolved crate is transferable; containers backed by
unimplemented leveled-list records stay explicitly locked. It does not claim
actors, damageable combat, simulated projectiles, or a complete campaign.

Run the complete repository gate from the repository root:

```powershell
pwsh -File scripts/Test-GodotRuntime.ps1
```

Pass `-FalloutNewVegasData` to make the gate validate the owned master and BSA,
extract the model directly, build a temporary cache, load it in Godot, and
delete the cache afterward. No retail-derived file or generated conversion
belongs in Git.

Build an asset-free experimental Windows archive after installing the pinned
Godot Mono export templates and `content/requirements-build.txt`:

```powershell
pwsh -File scripts/Build-GodotRuntime.ps1 -OutputRoot D:\Builds\OpenNV
```

The archive contains the Godot executable and a packaged legal-content helper,
but no commercial content. On first launch, select a legal Fallout: New Vegas
`Data` folder; OpenNV prepares its private cache and enters the playable saloon
sandbox. Python and OpenMW are not required on the player's machine. Later
launches reopen that verified cache automatically.

Use WASD and mouse-look, press E to pick up items, open containers, or operate a
door, left-click to fire an equipped weapon, and F5 to save. The HUD tracks the
four-stage sandbox objective and inventory. Packaging proves the route and a
separate cold reload before accepting the build.

Add `-FalloutNewVegasData <path>` to the build command for a local end-to-end
gate of the exported executable, packaged helper, legal cache, and Godot load.
