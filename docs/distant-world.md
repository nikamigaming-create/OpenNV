# Distant world publication

OpenNV prepares LAND vertices, source triangle order and face-material identity
on the shared bounded content workers. Managed preparation is retained with the
source transport in process. Native texture publication, quadrant tangent and
mesh creation, and collision construction advance separately. No transformed
retail content is a persistent launch input.

LAND shot impacts use the source horizontal hit position to select a terrain
vertex, then its strongest runtime texture weight. The point query retains source
triangle tie order and reassigns a snapped shared vertex to the preceding quadrant.
The residual base and normalized alpha weights are prepared once with the terrain.
Player and NPC shots use the same owner. A per-face material table cannot resolve
painted blends and is retained only for diagnostics. Static FNV analysis supports
the selection rule; matched retail impacts and global cell-edge selection remain
unverified. The observed selector examines six runtime slots, including the base;
the sixth authored alpha still contributes to normalization but is not selected.

Near-cell and distant-world publication share a two-millisecond CPU admission
allowance. When both have work, first admission alternates each frame. An
indivisible native operation can exceed the allowance; this is not a hard GPU
or whole-frame bound. Godot handles final frustum rejection, including its XR
views. Preparation priority uses horizontal forward bounds and apparent span;
it does not discard objects behind the head. Demand updates during pending
loads and cancels obsolete preparation without waiting on the gameplay thread.
Previously admitted coarse coverage remains until its replacements are ready.

## Object atlas cutouts

The owned WastelandNV building atlas is BC2/DXT3. GPU comparison verifies all
12 authored mip levels: 2,796,203 alpha samples, including 381,200 transparent
and 124,746 fractional samples, with no differences. The former shader discarded
only values below 1/255, making almost every filtered fringe opaque.

The object pass now accepts alpha greater than 128/255, with blending disabled
and depth writes retained. This neutral runtime cutout contract is corroborated
by the FNV-specific [Fallout LOD Fixes preparation owner](https://github.com/WallSoGB/Fallout-LOD-Fixes/blob/main/LOD%20Fixes/main.cpp).
That authored repair installs alpha testing during runtime preparation; the
serialized LOD NIF need not contain NiAlphaProperty. This is a documented
presentation repair, not evidence of vanilla's exact pass state or pixel parity.
Terrain does not inherit the object cutout. The actual production shaders are
checked at alpha bytes 0, 128, 129 and 255 in the native renderer.

No generated box substitute was found in the ordinary FNV distant-world path.
Property-free auxiliary terrain geometry remains an explicit divergence until
its source world owner is established; it is not assigned an invented material.
Physical headset frame pacing and matched retail pixels remain separate evidence.

## Cancellation lifetime

Cancelled grid and actor reads remain tracked until session retirement joins
them. Detached prepared actors are explicitly unbound before freeing: they have
never entered the scene tree, so `_ExitTree` cannot do that work. Completed
staged models are retired before source handles and engine shutdown. Ordinary
exported boundary walks exercise pending-stream quit and preserve the original
checkpoint. These checks do not establish a whole-game frame-rate target.
