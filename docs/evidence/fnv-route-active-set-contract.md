# Fallout: New Vegas bounded route active-set contract

Status: **initial native lifecycle for the prepared three-CELL route; ordered
transition and cold-restore acceptance remain pending**.

## Source boundary

The admitted route is the existing hash-bound XTEL graph:

```text
00103df9 Doc Mitchell's house
  00103e61 <-> 00103e69
000daebb Goodsprings exterior active set
  0010636f <-> 0010618e
00106185 Prospector Saloon
```

The runtime does not invent adjacency. `CellActiveSet` derives each neighbor
relationship from the reciprocal portal links already validated by
`CellSceneLoader`. A CELL is active when it is the authoritative current CELL or
is directly connected to it by one of those prepared links.

| Current CELL | Active prepared spaces | Suspended prepared space |
| --- | --- | --- |
| `00103df9` | Doc house, exterior | saloon |
| `000daebb` | Doc house, exterior, saloon | none |
| `00106185` | exterior, saloon | Doc house |

This preserves the directly adjacent space needed for an open-door view and
portal arrival while preventing a distant prepared interior from continuing to
render, process, collide, or illuminate the current space.

## Runtime ownership

Each prepared CELL contributes its CELL root, any top-level placed-reference
roots outside that root (currently the saloon's dynamic pool balls), and its
authored point/directional lights. The lifecycle owner captures their admitted
state once and applies five changes atomically:

- CELL-owned roots restore or suppress source visibility;
- root processing restores or becomes disabled;
- collision objects restore their source layers or receive layer zero;
- rigid bodies restore their source freeze state or become frozen;
- CELL-owned lights restore source visibility or become invisible.

`CellPortalTravel` changes the authoritative campaign CELL first, then applies
the matching active set. Initial load applies the same rule after saved CELL
validation, so a cold Continue does not briefly promote the root CELL as the
authoritative space.

## Native evidence

The direct Godot load used the separate, previously prepared v4 route cache only
after its model, sidecar, cell-scene, actor-scene, and opening-manifest hashes
matched that cache's install manifest. It did not invoke the content compiler or
write to the cache.

This was a direct `--cell-scene` lifecycle proof, not normal prepared-runtime
acceptance. The scene's embedded compiler identity predates the active source
compiler, so normal cache restore correctly rejects it. The proof establishes
the initial resource lifecycle against hash-closed owned content; it does not
establish launcher entry, ordered portal traversal, or cold restore under the
current compiler identity.

- report: `D:\Builds\OpenNV-active-cell-route-20260829-r1\initial-active-set-report-r2.json`
- report SHA-256: `851459f2a9c37a5f05f597b6875c682e459a9e25c844386ea6945a0c730d2057`
- Godot result: `OPENNV_GODOT_CELL_PASS`
- loaded scope: 4,239 references, 31 doors, 57 authored lights, 3,104 collision
  meshes, two reciprocal portals
- authoritative current CELL: `00103df9`
- active: `000daebb`, `00103df9`
- suspended: `00106185`
- suspended saloon state: zero visible roots, zero processing roots, zero enabled
  collision objects, all four dynamic pool-ball bodies frozen, and zero visible
  lights

No owned or derived media is committed by this evidence record.

## Cache behavior

`LegalAssetPreparer.TryRestore` is read-only. A missing, corrupt, or
compiler-incompatible cache now fails restore without calling the preparer.
Only an explicit prepare operation may create or replace generated content.
This prevents an ordinary launch from silently turning into a full cache rebuild.
The remaining compiler-family split is required so an opening-only compiler
change does not invalidate unchanged world output.

## Remaining boundary

All three prepared spaces are still instantiated eagerly before the lifecycle
owner suspends the distant one. This slice does **not** claim demand loading,
unloading, neighboring exterior-grid streaming, CELL-specific environment or
weather switching, ordered or reverse-route input acceptance, cold-restore
acceptance, Sunny behavior, launcher acceptance, or retail parity. Those remain
separate promotion gates.
