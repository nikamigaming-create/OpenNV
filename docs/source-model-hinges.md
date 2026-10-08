# Source model hinges

The reported spinning globes in the ordinary FNV opening had two dynamic source
bodies but no native joint. The collision builder pinned every body with a
constraint. This put the globe's static collider underneath its moving stand
and let their overlapping shapes drive the stand indefinitely.

Model collision now retains each source body's declared motion and mass.
After building the complete model, C# resolves the hinge's two body identities
and local pivot/axis frames. Godot binds a native hinge and excludes collision
between that joined pair. Basic hinges retain free axial rotation; limited
hinges retain their declared angle interval and maximum opposing torque. Blend
collision keeps its existing animated owner. Other joint kinds, missing body
endpoints, malformed frames and unsupported motor declarations reject visibly.
No model name, placed reference or location selects this behavior.

Joint models instantiate fresh native body/joint owners from the shared decoded
source. An instance cannot retain another instance's configured body references.
Tree exit frees the physics-server joint; tree reentry binds it again. Detailed
live state exposes source body identity, pose, velocity and joint pivot/axis
agreement without changing gameplay authority.

Synthetic contracts cover both owned NIF user-version layouts, body identities,
local frames, ignored vector padding and malformed input. Separate read-only
owned-file checks cover the globe and bucket in standalone FNV, standalone FO3,
TTW essentials and the combined JAM/TTW/NMC stack. Native owned-model checks make
two independent instances fall and settle on ordinary collision, verify free
hinge rotation before floor contact, retain their pivots/axes and leave the
decoded prototype unchanged. The globe's maximum measured pivot separation is
4.38 mm; the bucket's is 7.33 mm in those checks.

In the rebuilt ordinary FNV room, both globe joints and the bucket joint bind;
their source pivot gaps are below 0.1 mm and angular speeds below 0.0012 rad/s
in the selected settled state. Inspected pixels show the globe upright on its
stand in the source location. Recording is off and temporary pixels are deleted.
This fixes the observed runaway motion. Matched retail solver behavior,
nonuniform model scaling, other joint kinds and persistent generic loose-object
physics poses remain unverified. Existing actor ragdoll persistence has a
separate owner; the hinge checks do not establish complete physics/save parity.
