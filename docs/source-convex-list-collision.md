# Source convex-list collision

`bhkConvexListShape` retains an ordered child graph. Each decoded convex leaf
publishes its own native shape under the original rigid body. An aggregate hull
would fill authored gaps and change contacts. C# owns decoding, source identity,
body/child associations and material admission; Godot owns those native physics
objects. Every failed partial assembly frees its already-created body and leaves
while preserving the original source error.

## Source fields

The Fallout 20.2.0.7 block has a `37 + 4 * childCount` byte extent:

| Field | Encoding |
| --- | --- |
| Child count and ordered child references | UInt32 count, Int32 block references |
| Material and shell radius | UInt32, Float32 |
| Opaque integer and opaque float payload | UInt32, four opaque bytes |
| Child property data, size, capacity/flags | Three UInt32 values |
| Cached AABB choice | Canonical byte boolean |
| Closest-point minimum distance | Float32 |

The independent [NifTools format declaration](https://raw.githubusercontent.com/niftools/nifxml/master/nif.xml)
and read-only owned declarations inform this layout. Opaque values remain
opaque; the reader neither interprets them as process pointers nor substitutes
defaults. Complete field-read coverage, exact re-encoding and the block extent
remain separate checks. The source table is bounded by backed records and the
existing reader budget; an authoring-tool suggestion does not narrow its count
to eight bits.

## Runtime ownership

The body retains its original mass and full declared world/info filters.
Source filter metadata is inspectable, but metadata does not establish complete
Havok filtering behavior. This slice preserves the existing native world-layer
mapping and leaves its broader parity requirement open.

Child transforms compose through the existing source transform owner. Nested
collections retain their complete source lineage on each leaf. The original
child shell belongs to that leaf; the list radius remains a distinct declaration
and is not added to the child margin a second time. Radius alternatives and
Havok query-agent semantics need independent matched evidence. Concave packed
children, empty runtime aggregates, cycles and malformed declarations remain
visible errors. Compound material disagreement refuses a hit-material query
until parent/leaf precedence is known.

Cached AABB, closest-point thresholds, collision agents, native dynamics and
solver equivalence are independent from union geometry. Their declaration
retention is not behavior acceptance. Convex-transform wrapper material
precedence also retains the existing independent boundary.

## Acceptance

`FalloutNifPhysicsContractProbe` exercises the complete reader, distinct source
fields, opaque payloads, supported header versions, cached alternatives, nested
and transformed graphs, a backed wide table and malformed refusals.
`NativeNifInstanceAudit --convex-lists` requires genuine public/native contacts
for authored independent solids, a clear gap, transformed and nested contacts,
256 native leaves, original dynamic mass, independent resources and clean
partial-failure retirement. Its dynamic cases freeze before entering the world;
they verify construction and mass rather than solver behavior.

`--owned-convex-lists <owned-root> <model> [--mod-stack <json>]` assembles the whole
winning owned model, requires each original body and convex descendant, observes
contacts with the actual decoded leaves, retires two independent instances and
checks unchanged source bytes and no orphan/resource growth. Original models
and geometry remain read-only inputs. Other unowned model owners still reject
the whole assembly. This route does not bypass them with a collision-only model.

Ordinary source door/light interaction, authoritative animation/collision
motion, player/NPC traversal, complete saves, cold continuation and matched
retail physics/material/query/pixel evidence remain required independently.
Component fixtures do not close campaign or scene parity.

Actual Debug/Release reader and native authored contacts pass. The original FO3
vault door and operating light separately pass complete-model contact checks,
independent construction and clean natural retirement with unchanged source
bytes. Ordinary controller motion of those two models remains unverified.
The full required runtime gate passes. A fresh ordinary FO3 replay reaches the
original book allocation, Dad's route, CG01 stage 100, birthday stage 12 and
Amata's greeting. It retains independent missing model/AI/save owners, black
source UI text and a modal bot timeout; the whole scene is not accepted. Motion
of the selected convex door/light remains an independent unverified behavior.

`--convex-list-sources <owned-root> [--mod-stack <json>]` discovers winning NIF
resources through the same selected content resolver. It inspects headers and
convex-list declarations, records every refusal, then exits with failure if any
remain. Actual FO3 discovery inspects 17,153 of 17,163 models and finds 71 lists
in 66 models; ten unsupported headers remain. FNV inspects 20,532 of 20,542 with
zero lists found and ten header refusals. Independent TTW and combined selections
each inspect 32,518 of 32,537 with zero lists found and 19 refusals. Those failed
rows prevent a complete absence or source-support claim. Only the two selected
FO3 models have this slice's owned native contact proof. Remaining header,
whole-model and runtime behaviors retain their own acceptance gates.
