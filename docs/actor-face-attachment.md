# Rigid FaceGen component assembly

Winning RACE head-component models enter the FaceGen assembly path independently
of biped equipment. An unskinned face component does not need the `Prn` extra
data used by ordinary rigid equipment. Owned child mouth models omit it.

The receiving actor supplies its actual head bone and the inverse bind from the
selected skinned head model. Rigid face vertices use that model basis and follow
the animated head. Export-node transforms are replaced, retaining the geometry's
source translation without multiplying its rounded export scale into the bind.
Explicit parent markers still require a matching source head inverse bind.
Missing bones, missing binds, skinned implicit components, unsupported hierarchy
state and ordinary unowned rigid parts fail before retaining any partial nodes.

The skin material retains the source PP lighting flag `0x40`. The observed PP
lighting pass selector does not consume it; the separate no-lighting property
family uses that bit for angular falloff. It is not the alpha-texture flag.
Other unsupported FaceGen flags, controllers and render states still reject.
The source flag words remain available in material metadata.

`NativeNifInstanceAudit --facegen-attachment` verifies explicit and omitted
markers, a nonidentity inverse bind, replaced export transforms, animated head
motion and rejection cleanup. `NativeActorPerformanceAudit --appearance-stack`
assembles selected placed actors from the complete mounted plugin/resource
graph, including owned materials and source inventory. The three birthday
children pass that audit with all selected parts. These are component checks;
matched retail pixels, ordinary birthday completion and physical OpenXR
acceptance remain unverified.

Rigid head equipment uses its separate biped basis whether its source model
declares `Prn = Bip01 Head` or omits the marker. The owned biped slots include
eyeglasses, including equipment that combines eyeglasses and mask slots. A
different explicit parent retains its source binding; skinned equipment and
FaceGen inverse binds retain their existing owners. Attachment state resets
between source roots.

`NativeNifInstanceAudit --head-equipment-attachment` checks both marker forms,
animated head motion, skinned equipment and missing-parent rejection.
`--owned-head-equipment <game> <mod> <root> <npc> [dependencies...]` checks actual
owned rigid equipment, source materials and unchanged bytes. The selected TTW
Jonas glasses and the existing FaceGen regression pass. These component checks
do not establish matched retail pixels.
