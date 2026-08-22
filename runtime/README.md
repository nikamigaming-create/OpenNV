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
Godot Mono export templates:

```powershell
pwsh -File scripts/Build-GodotRuntime.ps1 -OutputRoot D:\Builds\OpenNV
```

Launching the exported executable without configured assets displays an
explicit experimental/non-playable status instead of crashing or claiming a
campaign is ready.
