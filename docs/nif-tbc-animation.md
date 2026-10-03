# Source TBC animation channels

NiTransformData scalar, vector and XYZ scalar channels admit source key type3
with their authored tension, bias and continuity. The shared sampler derives
outgoing/incoming Kochanek-Bartels tangents from neighboring values and source
time intervals, then evaluates Hermite interpolation. Endpoint chords continue
one-sided. Exact keys, initial/final clamps and single-key channels retain their
literal values. No model or reference identity changes this behavior.

The full reader validates channel extents and finite values. Admission rejects
missing/nonfinite parameters and nonincreasing/nonfinite intervals; sampling
rejects overflow. Nonconstant scalar/vector fixtures exercise nonzero parameters,
irregular times, endpoint parameters and exact key/clamp behavior, plus malformed
inputs and allocation-free per-frame sampling.

The selected owned door model has two source sequences and26 transform channels;
ten contain scalar/vector TBC. Its controller-channel audit samples3146 finite
poses, observes motion and leaves source bytes unchanged. The new ordinary cold
cell admission no longer retains its previous type3 refusal. Native moving-door
collision and matched retail timing still require the live route; sampler
acceptance alone does not establish them.

Neutral source contracts: [NIF format declarations](https://github.com/niftools/nifxml/blob/develop/nif.xml),
[Kochanek-Bartels spline mathematics](https://www.geometrictools.com/Documentation/KBSplines.pdf).
