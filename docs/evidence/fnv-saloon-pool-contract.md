# Goodsprings Saloon pool contract

Status: unsupported gameplay extension. Retail identities and individual NIF
physics are resolved, but a playable table layout, collision/contact, pocket,
reset, and cold save/restore have not passed the native gate. Physical OpenXR
acceptance is also pending.

The legally owned `FalloutNV.esm` places one ruined table, one cue, one rack, one
cue ball, and three object balls in `GSProspectorSaloonInterior`. Their reference
FormIDs, base FormIDs, models, transforms, and NIF rigid-body fields are compiler
inputs. OpenNV does not synthesize extra balls or guess their placement. The
authored ball references decorate the ruined retail table; they do not form a
playable layout on the intact replacement table.

The recipe explicitly replaces the ruined table presentation with the intact
retail `clutter\\billiards\\pooltable.nif` asset. This is an OpenNV practice-table
extension, not a claim that retail New Vegas presents an intact table there.
The intact model retains its packed-triangle collision evidence, but that MOPP
does not contain a playable cloth bed. The pool recipe therefore declares
`presentation-render-triangles` as the gameplay collision source. Godot builds
the static table collision from the intact model's exact authored triangles;
no box, plane, or other proxy geometry is introduced. This retains the visible
felt, cushions, and pocket openings as the physical surface.

Ball collision is exported from each NIF `bhkConvexVerticesShape`. Mass,
friction, restitution, damping, motion system, quality, and filter metadata are
retained. A placed reference owns the object's world transform; serialized
dynamic body translation and rotation are retained in the sidecar as evidence
and are not applied as a second placement transform.

Desktop and OpenXR input are adapters over the same Godot rigid bodies. Desktop
uses configured strike speeds. OpenXR measures the tracked authored cue tip's
movement and converts a valid sweep into an impulse. All non-retail tuning and
mount transforms live in `runtime/config/open-nv-runtime-v1.json`.

This slice is solo practice with the four authored balls. Full eight-ball rules,
AI, and a fabricated fifteen-ball rack are outside this contract.

The flat and OpenXR-layout software gates are designed to strike the same
authoritative rigid bodies, require an observed ball-to-ball collision, move
one owned object ball below the table's pocket boundary, require it to retire,
cold-load that pocketed state from the shared save, restore it into the live
table, and finally reset every ball to its exact authored transform. The flat
gate currently fails before contact because at least ball `00107d76` has no
intact-table triangle beneath its authored X/Z position. OpenXR-layout proof is
therefore not run. A future playable arrangement needs an explicit, reviewable
OpenNV policy contract; it must not be inferred and presented as a
retail-authored layout.
