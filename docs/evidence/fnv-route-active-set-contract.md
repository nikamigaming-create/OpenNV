# Fallout: New Vegas bounded route active-set contract

Status: **current-CELL-only lifecycle accepted across the prepared three-CELL
forward route and cold Continue**.

## Source boundary

The admitted route is the existing hash-bound XTEL graph:

```text
00103df9 Doc Mitchell's house
  00103e61 <-> 00103e69
000daebb Goodsprings exterior active set
  0010636f <-> 0010618e
00106185 Prospector Saloon
```

The runtime does not invent adjacency. `CellActiveSet` validates the reciprocal
portal links already admitted by `CellSceneLoader`, but only the authoritative
current CELL is active. Linked CELLs remain instantiated and hash-bound while
their roots, processing, collision, rigid bodies, and lights stay suspended.

| Current CELL | Active prepared space | Suspended prepared spaces |
| --- | --- | --- |
| `00103df9` | Doc house | exterior, saloon |
| `000daebb` | Goodsprings exterior | Doc house, saloon |
| `00106185` | saloon | Doc house, exterior |

The bounded route has no source-proven portal clipping or room-visibility
contract. Rendering adjacent spaces wholesale mixed interior shells with the
Goodsprings exterior and allowed inactive collision to occlude the player.
Portal travel therefore changes the authoritative CELL and its collision mask
atomically instead of depending on a simultaneously visible neighboring shell.

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

The normal owned menu/Continue path reused the admitted four-family cache at
`D:\Builds\OpenNV-fnv-articulated-convex-cache-20260829-r1`; it did not invoke
the compiler or write to that cache. Configured Godot input crossed both ordered
XTEL pairs, saved in the saloon, and a separate headless process cold-restored
the same CELL with zero replayed transitions. Both reports pass the existing
manifest-backed validators.

The accepted private evidence is
`D:\Builds\OpenNV-fnv-current-cell-route-acceptance-20260829-r1`:

- first-run report SHA-256:
  `c29244494ae4962ac82dbffcc47795084c56e72c4bac3af86136324ea8fff6db`;
- cold-Continue report SHA-256:
  `9f3f46a533db0629473b681bb9cfea1c369041616c7f5f9ce5083475b6e5aa06`;
- resulting save SHA-256:
  `289891ae3eb36024fe165b37ce9376c8c27e908a070c5d661c78b936aa1a6d03`;
- first-run console SHA-256:
  `cd48e8f5c9b9cbd37f5c83192edb9ed6eb0f9360089e6b3e2a72487549a951de`;
- cold-Continue console SHA-256:
  `3cecad629a8cd074f2a7508bb6508394416dd36849295acbb459941bdc8a65db`.

The first-run active-set updates are exactly Doc house only, exterior only, and
saloon only. The same run records authored-collision support corrections for Doc
`00104c0f`, Easy Pete `00104c80`, saloon actor `00104f08`, and Sunny
`00104e85`. Those corrections remove the systemic floating-root defect for the
currently admitted actors; they do not establish AI, population completeness,
animation, or visual parity.

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
owner suspends the two noncurrent spaces. This slice does **not** claim demand
loading/unloading, open-door portal views, neighboring exterior-grid streaming,
reverse traversal, integrated OpenXR acceptance, complete actor population or
behavior, or retail visual parity. Those remain separate promotion gates.
