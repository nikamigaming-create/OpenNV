# Fallout: New Vegas retail material/shader contract

This is a clean-room observation contract for OpenNV's Godot material path. It
records behavior from a legally owned retail install; no retail shader bytecode,
texture, mesh, or executable data belongs in Git or in an OpenNV release.

## Source and boundary

- Observed package: `Data/Shaders/shaderpackage019.sdp`
- SHA-256: `31f7f53020692172f837207023f0f471a5097b453bf3ee35070081c774ada0c8`
- Package header: package `100`, 1,007 named Direct3D 9 shader programs
- Verification: package records were bounds-checked to the exact source length;
  selected owned pixel/vertex programs were disassembled locally with the
  Microsoft Direct3D shader disassembler.

The derived equations and register roles below are compatibility facts. The
compiled programs remain private retail evidence.

## Recovered behavior used by OpenNV

The SLS environment pass reconstructs a tangent-space normal, reflects the view
direction, samples the authored cubemap, chooses the custom environment mask or
normal-map alpha, multiplies by the authored environment scale, applies vertex
color when enabled, and fades with the CELL depth-fog curve. It is a separate
material pass. Environment texture slots are inactive unless the NIF shader flag
enables environment mapping. Materials requiring the separate retail
`envmap-light-fade` term stay off that pass until that term is recovered.

The ordinary lighting family samples base and normal maps, applies radial light
attenuation and CELL ambient, then uses the material self-illum input. A white
`NiMaterialProperty.emissive_color` alone does not establish active self-illum:
the observed lamp and jukebox surfaces carry a
`NiMaterialColorController` targeting `SELF_ILLUM`, while ordinary safe geometry
with the same white material default does not. A controlled no-glow-map surface
replaces its lit result with the controller color instead of treating the base
texture as an emission map.

## Runtime mapping

- `NiAlphaProperty` flags determine opaque, blend, or alpha-test mode and cutoff.
- Vertex RGB/alpha are enabled only by their retail shader flags.
- `BSShaderNoLightingProperty.file_name` and legacy `NiTexturingProperty` sources
  are retained.
- DDS cubemaps retain all six faces and are converted from Gamebryo Z-up face
  order to Godot Y-up order.
- Material binding uses the imported NIF/glTF surface name, never list position.
- CELL fog color, near/far depth, and power drive Godot depth fog.
- Cell scene v5 and actor scene v3 reject older incomplete contracts. Actor v3
  also hashes the glTF, sidecar, and binary buffer independently.

## Still failing closed

Retail external-emittance color, environment-map light fade, the complete
non-PBR point-light equation, HDR adaptation/color grading, and exact projection
remain open. Current frames are renderer diagnostics, not retail-fidelity proof.
