# OpenNV Godot runtime

This is the first-party Open Nevada runtime. It uses Godot Forward+ and accepts
only artifacts produced by the direct retail-content pipeline in `../content`.

The current slice reads the owned master directly, resolves one recipe-pinned
interior CELL and its placed REFR graph, exports 14 structural/door NIFs, and
loads 42 data-positioned references in Godot. The incoming door's XTEL target is
the spawn origin; runtime trimeshes provide the first collision proof. It does
not claim textures, retail collision blocks, animation, actors, or campaigns.

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
but no commercial content. Launching it displays an explicit
experimental/non-playable screen with a native Data-folder picker. Selecting a
legal Fallout: New Vegas `Data` folder prepares a private cache and immediately
loads the first retail geometry slice; Python and OpenMW are not required on the
player's machine. Later launches reopen that verified cache automatically. If a
runtime update changes the packaged compiler, OpenNV rebuilds the cache from the
remembered read-only installation before loading it.

The cell slice has basic first-person movement. Use WASD and mouse-look, press E
on a door to open or close it, and use the left mouse button for a physical ray
query. The gate proves that the XTEL spawn lands on floor collision and that the
same short ray hits a closed interior door but passes after it opens. OpenNV
loads the whole cell instead of inserting a fake opaque portal plane.

Add `-FalloutNewVegasData <path>` to the build command for a local end-to-end
gate of the exported executable, packaged helper, legal cache, and Godot load.
