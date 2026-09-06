# Morph lighting basis

The face flash during blinking came from the renderer representation of
relative blend shapes. OpenNV supplied zero normal and tangent deltas, intending
to preserve the source lighting basis. Godot packs these fields as unit
directions; zero vectors do not survive that representation. Applying a blink
therefore added an unrelated direction across the whole face.

The importer now supplies absolute vertex targets and the unchanged source
normal/tangent basis with normalized blend-shape mode. For base vertex V,
source deltas D and weights w, this gives V + sum(w D). The repeated basis
cancels the base-weight adjustment independently of overlapping or signed
weights. Source morph values and blink scheduling are unchanged. This applies
to NIF morph attachments and FaceGen parts through the same mesh builder.
Recomputing expression normals remains a separate unsupported lane.

The renderer's packing and blending contract is visible in Godot's
[skeleton shader](https://github.com/godotengine/godot/blob/master/servers/rendering/renderer_rd/shaders/skeleton.glsl).
Verification uses the installed renderer, independently of that source link:

- A synthetic NIF goes through the real importer, packed mesh readback and
  baking with isolated, overlapping and signed expression weights. Positions
  retain additive source displacement; normals and tangents retain their basis.
- Replacing only the mesh builder with its prior implementation makes that
  regression fail specifically on the lighting normal.
- Forward+ checks 18 rendered samples across normal, tangent and binormal
  output with the same weight cases. Doc's ten owned morph surfaces also pass
  packed-basis checks, and the actor audit exits cleanly.
- Ordinary room-77 retains a 16-second post-creation replay with 952 OpenNV
  frames, no missing OpenNV transport frames and no queue overflow. Blink
  telemetry advances seven cycles. The inspected open/closed/reopened frames
  no longer show the whole-face whitening.

The replay is not a matched retail comparison. Its retail stream has 14
missing transport frames; audio was not recorded. Complete blink phase,
expression normals, skin/lighting response and final retail pixels remain open.
Owned inputs, recordings and diagnostic artifacts stay private.
