OPENNV INSTRUMENT MK II V2.1 / DIAL-ONLY CORRECTION

Open opennv_instrument_mkii_v21.blend or import opennv_instrument_mkii_v21.glb. These are complete standalone instrument assets with packed/embedded textures. Separate original PNG dependencies are included unchanged.

Only the selector labels, four markers, marked pointer shape and angle metadata changed. Static body geometry, materials, all other controls, screen UVs and the selector control pivot/rest basis were preserved. Native four-detent proof images show each actual physical selection, not an engine run.

Application order: fallout1, fallout2, newvegas, fallout3.
Front angles (right 0 degrees, up 90 degrees): 135, 45, -45, -135.
Corresponding local rotations: -45, 45, 135, 225 degrees.
Axis: Blender local +Y = Godot local -Z. Positive rotation is CLOCKWISE from the front. Detents are 90 degrees apart. Wrap is 360 degrees; 315 is equivalent to -45 for the return to Fallout 1. At neutral rotation zero, the distinct pale pointer tip points UP. The old -0.62 radian lever child tilt is removed.

integration_contract_v21.json is authoritative for the exact selection map. dial_v21_validation.json records measured tip-angle alignment and unchanged non-dial geometry/transforms. Local input behavior and Godot runtime are still consumer-owned; this package does not claim runtime verification or final art approval.

Editable patch source: apply_dial_v21.py. To reproduce this bounded patch, place the previous v2 source folder alongside this folder under its original name opennv-launcher-sculpted-v2, containing opennv_instrument_mkii.blend and integration_contract_v2.json. The prior delivered v2 package is Library libfile_92513865b5308191a6632440c47b424a. The final v2.1 blend/GLB themselves do not require that prior folder.
