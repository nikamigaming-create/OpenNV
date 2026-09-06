# Material colours, fog, angular opacity and late attachments

These are bounded rendering contracts, not a scene-parity claim. Owned shader
programs and private native observations remain local; the runtime evaluates
general source fields and never reads comparison measurements as authority.

## Reference material emittance

REFR.XEMI resolves through the winning record graph. A LIGH contributes its
source RGB conversion; a REGN samples the existing shared sky/time owner.
Loading a material does not choose or change the region's weather. Unsupported
record kinds, absent owners and invalid colour samples fail closed.

The authored external-emittance shader flag admits a surface. Per-reference
instance uniforms replace its emissive RGB before the authored multiplier;
they do not mutate a shared material. The owner binds initial and later
geometry, follows transfers, clears removed bindings and isolates preview
viewports. Clock changes update bound values without a per-frame scene walk.

No-lighting surfaces use this same source material path with or without angular
falloff. The previous split sent non-falloff windows through a default material,
omitting their emittance. Managed colour and UV channels now target source
shader parameters. The native no-lighting colour rule substitutes white only
when the entire resolved RGB is zero; individual zero channels, small nonzero
colours, overbright values and source alpha remain intact.

The selected owned reference audit, private native properties and ordinary
room-66 instance trace agree on all 20 material RGB triples and multipliers.
This includes nine window surfaces and eleven angular effects. Native program
observations corroborate the no-lighting colour rule. Native selected GPU draw
execution, general HDR-mode multiplier policy, fog/blend composition and final
frame correspondence remain separate open evidence lanes.

## Lit vertex fog

The selected owned lighting programs compute distance from projected vertex
coordinates before perspective division. The previous view-space length loses
the projection's lateral scaling and depth convention. The shared GLSL owner
converts the renderer's reverse-depth coordinate to forward depth, measures the
projected vector and applies the authored near/far/power curve at each vertex.
Rasterization interpolates that factor. Perspective distance converts back to
game units; orthographic clip coordinates are dimensionless.

The NIF lighting instances retain original game-unit fog ranges and an explicit
reciprocal unit scale. Other supported lit/static/terrain/actor shader owners
share the projection helper. Grass retains its separate source program policy.
This does not establish native shader selection, fog toggles, camera frustum or
final fog contribution for every draw.

## No-lighting angular opacity

The owned falloff variants derive the absolute cosine between normalized view
position and transformed vertex normal. The authored cosine endpoints map it
to a clamped fraction t; opacity interpolates the authored endpoints with
t squared times (3 minus 2t). This occurs at the vertex stage. The previous
fragment-stage linear interpolation changed the shape and brightness of source
window effects. Texture, material and vertex alpha retain their source owners.
No-lighting fog/blend toggles and active native variant selection remain open.

## Cell environment lifetime and provenance

Source NIF instance uniforms previously received the cell environment only
during initial construction. Later animation objects retained zero defaults.
The cell owner now binds existing geometry and subscribes to scene insertion
for new descendants. A transfer receives the destination cell's settings.
Separate viewport scenes keep their own environment, and removing the owner
unsubscribes it. No per-frame scene walk is introduced.

Animation-object and skeleton roots retain their source model identity, so
their directly decoded geometry can be traced back to owned bytes. The render
trace queries all declared mesh instance parameters instead of a preset list;
that inventory exposed the late attachment's missing values.

## Verification

The required repository gate covers builds, analyzers, synthetic format probes
and native project loading. The selected owned fog audit is:

```powershell
dotnet run --project contract-tests/FalloutImageSpaceProbe -c Release -- --cell-fog $ownedDataRoot $cellHex
dotnet run --project contract-tests/FalloutPluginRuntimeProbe -c Release -- --audit-material-emittance $ownedDataRoot $cellHex $hour
```

The selected NIF rendering probe also accepts an owned data root and a text
file listing model paths. Keep the fixture list and owned outputs private.

The optional `res://tools/NativeVertexFogAudit/NativeVertexFogAudit.tscn` scene
uses a real local GPU rendering device. Launch it with the normal Forward+
renderer; the headless dummy renderer cannot run this audit. It exercises:

- 64 independent forward-depth projection expectations against the production
  shader helper, including perspective/orthographic projection, two fields of
  view, two unit scales and both renderer clip-space conventions.
- 64 exact smooth-opacity values, signed cosine symmetry and source endpoints.
- Discovery of arbitrary declared instance parameters without preset names.
- Existing and late geometry, subtree transfers, owner removal/re-entry and
  isolation of both existing and later preview geometry.
- Exact zero-colour fallback, preservation of nonzero/overbright colours and
  per-reference emittance lifetime without mutating shared materials.

These checks pass on the selected Godot build. Owned fog inputs and ten owned
angular-opacity variants independently support their respective contracts.
Ordinary input, live bindings and matched native/render evidence are separate
lanes. Region-error comparisons remain diagnostics when camera, animation and
frame timing are not aligned.

The latest material GPU assertions pass but shutdown reports three ObjectDB
instances; their types and owners remain unverified. The original Vigor audit
also passes all eight forward/reverse pages and allocation bounds after the
material-path change. These component results do not establish ordinary Vigor
timing, audio or visual acceptance.
