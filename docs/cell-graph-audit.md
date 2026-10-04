# Source cell and resource coverage

The development lab's `cell-graph` command reads winning CELL children, their
record and resource dependencies, and authored interior teleport links directly
from the selected owned stack. A seed is required. An optional EDID substring
adds all matching interiors, including disconnected and empty cells, before
following outgoing interior XTEL links. Teleport edges remain directed;
weak connectivity is used only to report disconnected selections. Exterior
destinations remain explicit boundary edges.

Every selected cell retains its effective reference denominator. References
that cannot be bound by the scene reader remain named source identities rather
than disappearing from the component summary. Winning deleted references are
reported separately. Source bytes, declaring masters, reference/base hashes,
placements, enable chains, model declarations, locks and destination links
remain independent of native presentation.

The enable audit evaluates both values of each independent root through the
shared reference-world owner, then restores the disposable state. It retains
opposite edges, engine-player constants and absent/deleted/taken cuts. Actor
package discovery follows all authored template and leveled-list alternatives,
retaining candidate order, schedules, conditions and event declarations without
choosing or executing a procedure. Actor appearance resources use the selected
saved or diagnostic appearance; arbitrary appearance and script-state resource
alternatives are not exhausted.

NIFs, KFs and DDSs are read and hashed in place. All NIF blocks and declared
texture dependencies are inspected, including source models on lights whose
native construction remains unsupported. Successful decoding does not remove
a separate rendering refusal. Resource, package and reference failures are
reported in separate, overlapping lanes.

Collision calculations use the ordinary placed-reference policy: TES placement
replaces the model root transform, while descendants retain authored local
transforms. The audit shares the runtime's body and transformed-shape math,
including its coordinate basis and Havok units. Packed triangles and box faces
can be projected against NAVM centroids or an explicitly supplied native point.
Convex, sphere and capsule declarations remain accounted for without inventing
triangle floors. These projections do not execute Jolt, filter contacts, sweep
an actor capsule or prove that a missing sample is a hole.

An optional checkpoint must match the selected stack's save compatibility
identity before any saved reference state is admitted. Failed restoration leaves
the source-only world separate. This join does not perform a complete cold load,
advance script events or establish reusable campaign continuation. An optional
native snapshot is joined only to the seed CELL and retains its original build
identity and capture time; it is not a current-build observation of other cells.

Full-reader synthetic fixtures check interior closure, exterior boundaries,
disconnected and empty selections, failed-cell reference accounting, modeled
light resource expansion, opposite enable roots and isolated save/snapshot
rejection. Independent expected coordinates check root replacement, descendant
rotation/scale, placement and configured units; multi-root placement refuses
instead of contributing invented support.

```powershell
dotnet tools/OpenNV.DevelopmentLab/bin/Debug/net8.0/OpenNV.DevelopmentLab.dll `
  cell-graph $installation $freshPrivateOutput `
  --seed $cellFormKey --runtime-config $configuration `
  --metadata $editorIdSubstring --checkpoint $checkpoint --snapshot $snapshot
```

`--metadata`, `--checkpoint`, `--snapshot` and `--sample-native x y z` are
optional. For a mod selection, append `--mod id root dependency-root ...` last.
The output directory must be fresh. Reports are private JSON inventories, not
converted launch assets. A completed audit returns 1 while audited failures
remain and 0 only when its selected blocking lanes pass; a CLI, configuration or
incomplete-component failure returns 2. Unverified lanes remain in
the reports even with exit 0. Neither outcome certifies every reachable state,
ordinary traversal, whole-level support or retail parity.
