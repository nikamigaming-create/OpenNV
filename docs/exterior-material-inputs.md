# Exterior material input contracts

These are independently implemented input and material contracts. They do not
establish complete exterior or matched final-pixel parity. Owned records,
shader disassembly and private executable observations remain machine-local.

The owned ordinary lit pixel programs multiply the diffuse image and enabled
vertex colour by the ambient, glow-emittance and light sum before fog and output
transfer. Emittance is therefore modulated by diffuse detail, including when no
separate glow image is authored. Adding an independent solid emissive colour
erases that detail on lit lamp globes. Authored material-colour controllers keep
their own values and timing; this correction does not change those animations.

The NIF [shader flag definitions](https://www.niftools.org/nifxml/BSShaderFlags.html)
identify authored single-pass decals and parallax. Authored decal geometry keeps
its own UVs, alpha property, depth flags, placement and collision. It is not a
request to generate another mesh or project a replacement decal.

Owned PAR vertex and pixel programs establish the parallax input contract:
normalize the vertex-to-eye direction in the source tangent frame, interpolate
it, then normalize it again. Read height red at the original UV; the offset is
the tangent direction's XY times `(height * 0.04 - 0.02)`. Diffuse RGB and normal
use the offset. Alpha coverage and glow retain the original UV. A missing
height texture or tangent frame remains an error. This does not establish
native pass selection, partial precision, shadows or complete lighting parity.

Owned rubble meshes retain a `Data` prefix in some texture names. Remove that
one installation-relative prefix after rejecting rooted or traversing paths;
resolve the remainder through the ordinary winning loose/BSA resource graph.

Private native landscape UV construction establishes a 17 by 17 quadrant with
an advance of `fLandTextureTilingMult / 4` per vertex on both axes. At the owned
default multiplier of two, a quadrant spans eight repetitions. The previous
runtime spanned 32, making the repeated pattern four times too small per axis.

Native LAND processing stores VTXT opacity values per vertex and texture. Its
material preparation inserts a base weight clamped from `1 - sum(alpha)` and
normalizes the painted weights when their sum exceeds one. The owned terrain
pixel programs sum weighted diffuse samples and normalize the weighted decoded
normal vectors. Normalization happens before vertex interpolation, not after
sampling or through a sequence of fragment `mix` operations. VCLR remains an
independent multiplier. Source shader programs and the private scalar setup
agree on these separate stages.

Placed static references register their managed animation groups without selecting
a looping group merely because it is the only alternative. A later explicit
source playback request can still select that group. Direct active controllers
retain their authored startup clock. This is separate from the placed animated
object startup path and from material emission strength or authored color curves.
