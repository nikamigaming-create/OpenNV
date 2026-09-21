# Environmental wind

Wind-responsive dynamic bodies use their NIF rigid-body flag, not a model name
or a hand-selected reference list. The owned tumbleweed carries bit 0, mass 2.5
and dynamic sphere-inertia motion. The source values survive prototype cloning.

The clean-room wind-listener contract uses normalized winning WTHR wind speed.
Each physics step draws a separate gust and heading perturbation for each body.
With strength `s`, uniform samples `u` and `v` in `[0,1]`, and step `dt`:

- Magnitude is `clamp((2u - .75) * 250s, 0, 250) * (1 + floor(dt / .0167))`.
- Heading is the sky heading plus `(2v - 1) * pi/4`.
- The source force points along positive Y, rotated around source Z.
- The body is activated before applying the force.

The native adapter converts Havok distance units to metres once. Godot applies
mass and integration time. Both flat and OpenXR use this same owner and source
contacts. The observed sky bootstrap heading is one radian. Weather transition
blending, exact random-stream identity and matched retail trajectories remain
unverified; this is not a physics-parity claim.

An exterior-scoped registry follows tree additions/removals. It excludes frozen
equipment, disabled references, inactive warm references and interior scenes.
Calm weather stops wake/force publication. The selected owned native test checks
sleep/wake, independent clones, frozen/interior exclusion, disable, warm reentry
and returning to calm. Synthetic tests cover the cap, weather normalization,
horizontal spread, step scaling and invalid-input refusal.

Run the native check with `NativeNifInstanceAudit.tscn -- --wind DATA MODEL`.
The private selected input is the owned tumbleweed NIF; no retail data is checked
in. Ordinary exported flat and Elliott Tate simulator telemetry shows active
forces and independent changing poses. One distant body falls below the terrain;
its collision/residency owner still needs investigation. Cold dynamic-prop
pose/velocity persistence remains unverified. This does not establish complete
outdoor behavior, and a warm-residency test does not establish cold continuation.
