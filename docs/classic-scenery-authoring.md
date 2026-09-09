# Classic scenery authoring

The current furniture set is first-party geometry authored by
`local/classic-authoring/build_classic_furniture.py` in Blender. This private
authoring helper reads no retail data and is not a product launch dependency.
Its GLBs contain geometry and neutral materials. Original FRM artwork is read
from the selected classic installation at runtime by `ClassicAuthoredScenery`;
wood, cloth and steel retain separate material responses. Original paint now
projects onto real mesh faces using the same classic projection that places
the model. A transient first-surface depth atlas keeps that paint off hidden
undersides and stacked surfaces; grazing/back faces use their own material.
Transparent border RGB is padded before mipmapping, with the original alpha
retained. Paint never changes geometry or creates holes. No extracted retail
texture is a packaged or persistent launch input.

Wear, wood grain and cloth weave use assembly coordinates measured in meters,
including mesh transforms and the final uniform source-fit scale. Moving or
orbiting the object does not slide the texture. The versioned Materials section
of the authored-scenery recipe owns physical detail scale, projection limits,
paint strength and material response. This is partial source-view texturing,
not a claim that one sprite contains faithful detail for every hidden surface.
The rear material and sectional furniture still require art work.

Nineteen furniture models cover twenty-three source artwork bindings: sleeping mats, bedrolls,
metal beds, salvage beds, full and sectional bunks, wall beds, small tables,
toppled/broken tables, long tables with benches, and the distinct vault bed, table and chair. A twentieth model supplies the stalagmite cluster binding. The bindings live in
`runtime/config/classic-authored-scenery-v1.json`. These are the first modeled
pass, not accepted one-to-one replicas. The six UNUSED ART bookshelf tiles in
the reference sheet deliberately have no furniture binding.

All source art direction/frame offsets participate in placement. The actual
source rectangle center is compared against the model's classic projection and
converted back to the shared ground plane. This preserves the source hex as the
anchor without blindly centering every 3D prop on it. The MAP's explicit object
pixel offsets and rotation remain separate inputs. Vault 13 beds and tables have now been inspected in the running living quarters.
Full visual acceptance is still required, especially for sectional furniture and
authored facing variants.

Rotated parts now project directly before their extents are combined; a larger
axis-aligned box around the rotated assembly no longer causes unnecessary
shrinking. The full bunk has a 90-degree authored correction to align its long
axis with the reference; sectional variants remain unfinished. A current native
MBSTRG12 inspection loaded all three bunk parts with the new shader and orbited
the room without shader errors. The private diagnostic is
`local/classic-runtime-video/materials/industrial-materials.png`.

Owned FNV and FO3 donor libraries have independent lifetimes and preserve their
own loose/BSA winning resource namespaces. A NIF's textures resolve from the
same selected game as the NIF. Recipe bindings may name an explicit SourceGame;
otherwise New Vegas is preferred, then FO3. Missing donors retain the source FRM
and report the missing binding. Character studio source changes no longer
dispose the world scenery libraries.

Source MAP object light radius/intensity are retained from full-21 words 13/14
and compact-17 words 9/10. Owned PRO light headers and placed FO1 lamp/fire/light
objects were inspected while implementing the field binding. Godot light height,
color, attenuation and shadow response are presentation adaptations. Original
floor paint now receives lighting, subtle interpreted relief, mipmaps and contact
occlusion. Fires and the selected original animated signs use their FRM clocks;
door and scripted animation state remains unimplemented.

The geology candidate uses an original built-in imagegen reference through the
user's local ComfyUI TRELLIS.2-4B-FP8 installation (seed 20260907, 512 shape path).
Blender repairs and closes its geometry, grounds it, and reduces its mesh. The
runtime applies the selected owned cave material. The prompt, workflow, source
generation, cleanup file and mesh provenance are retained under
`local/classic-scenery-workshop` for this active asset work.

The local furniture and geology sheets are Blender modeling renders. They are
not gameplay screenshots. The concept boards and their prompts are in
`local/fo1-world-concepts`; the complete map/elevation worklist is
[classic-world-realization.md](classic-world-realization.md).

The current vault wall path fits source ground contours rather than extruding
blocking hexes. Adjacent wall volumes share a union shell; original panel art
projects onto their vertical faces. Convex corners retain both planes. Doorway
trim keeps the original transparent opening and gains thickness around its
silhouette. These meshes remain source-placement adaptations; source door state
and activation still require the gameplay owner. Runtime recipes own thickness,
wall height and contour tolerances. Both floor rendering and floor lookup use the
original square-column order, correcting the mirrored floor underneath objects.

The source-contour metal wall family also admits `dv` and `mmb` variants. The
current MBSTRG12 level 1 loads 802 former wall sprites through the joined 3D
wall path. Its `mmb1037` contour remains unresolved; 347 other world objects
still use sprite references. These counts identify remaining work, not visual
acceptance. One mutant `mamtntka` direction/frame decode also fails; the source
identity is reported once and the last valid presentation stops advancing.

The private searchable asset inventory is
`local/classic-asset-inventory/latest/index.html`. It covers all 227 stored maps
and 338 elevations across both installed classic games, indexed art/prototypes,
world placements and nested inventories, alongside the two donor libraries.
Declared candidates are distinct from missing 3D work and original UI art;
35 initial source-resolution failures remain listed. This inventory predates
the `dv`/`mmb` binding expansion and does not establish script-spawned coverage,
all-direction animation validity, gameplay or campaign completion.
