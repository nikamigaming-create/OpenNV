# Tracked rifle diagnosis

The rejected October 17 physical playtest remains unaccepted. The user's mirror
recording shows an oversized-looking right hand and forearm while aiming a rifle
around 32:10-32:48. A two-dimensional mirror image alone does not distinguish
skin deformation, close-eye projection, tracking space and stereo presentation.
No offset, scale, asset substitution or arm-solver repair is justified yet.

`NativePlayerRigAudit` constructs the ordinary source appearance from a private
native save and selects a winning WEAP by editor ID. It uses the production
first-person attachments, full-body eye policy, shared torso and tracked arm
solver. Inputs are generated component poses, not recorded user motion, ordinary
gameplay, collision acceptance or an actual headset session.

The diagnostic reports source skeleton identity, named local/global rest and
posed frames, model/palette identities, weighted source-edge samples and mesh
transforms. Nine forward/near-eye/cross-body and roll poses check source upper-arm
and forearm lengths, retained first-person muzzle aim and native renderer palette
agreement when a renderer is required. A changed-length negative fixture must
fail. Large edge ratios are observations, not an anatomical pass/fail threshold;
authored linear skinning itself changes some edges.

Run the source project after its Debug build, with XR disabled:

```powershell
& $Godot --headless --path runtime --xr-mode off `
  res://tools/NativePlayerRigAudit/NativePlayerRigAudit.tscn -- `
  $OwnedData $PrivateSave WeapNVVarmintRifle $PrivateReport
```

An optional fifth argument is a fresh private capture directory. Use a native
renderer instead of `--headless` for that explicit visual check. It produces six
selected mono component views (eye and external views of three poses), not a
continuous recording or final OpenXR eyes. Keep source-derived reports/images
private; remove temporary images after inspection unless still selected for an
active investigation. Recording remains off in ordinary builds and checks.

The initial owned Vulkan rifle diagnostic retains segment lengths and native
skin-palette agreement in its nine poses. Near-eye positioning reproduces the
large projected hand in a mono view while its external view shows a coherent
arm. This does not establish the cause of the user's recorded defect or validate
stereo motion. A second owned pistol component check passes its selected length,
muzzle and negative checks headlessly; it supplies no native GPU evidence.

First- and third-person source hand rest bases agree in the selected male
skeletons, with a roughly 1.6 mm rest-origin difference. Their authored weapon
aim poses differ, so animation identity is not interchangeable with bind identity.
Do not replace one source palette or animation with the other based on names.

Godot's bone global pose is relative to its skeleton, so the diagnostic composes
it with the actual skeleton world frame. See the
[Skeleton3D contract](https://docs.godotengine.org/en/4.5/classes/class_skeleton3d.html).
Controller grip and aim are separate frames under the
[OpenXR pose contract](https://registry.khronos.org/OpenXR/specs/1.1/html/xrspec.html).

Next evidence: actual OpenXR grip/aim/head and submitted-eye frame joins, neutral
and moving stereo views, support release, weapon swap/reload, tracking loss and
an unassisted physical retest. Meta operator tools being marked offline is a
tool availability observation, not proof that the headset is disconnected.
