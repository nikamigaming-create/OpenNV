# OpenNV Godot runtime

This is the first-party Open Nevada runtime. It uses Godot Forward+ and accepts
only artifacts produced by the direct retail-content pipeline in `../content`.

The first promoted slice loads one static opaque NIF exported to glTF with a
hash-pinned `opennv-static-nif-gltf/v1` sidecar. It deliberately does not claim
collision, texture, material, animation, world, or gameplay support yet.

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
player's machine.

Add `-FalloutNewVegasData <path>` to the build command for a local end-to-end
gate of the exported executable, packaged helper, legal cache, and Godot load.
