# Fallout 2 Map 3 torch-animation source audit

Date: 2026-08-30

Scope: retained Map 3 elevation-0 presentation and ARVILLAG

Result: **no source FRM frame advance exists for the visible torch records**

This is an asset-free contract audit. It records identities and structure from a
legally owned Fallout 2 installation; it contains no game pixels, archive
members, executable material, or disposable-cache paths.

## Exact retained-slice evidence

The 22 visible torch placements in Map 3 elevation 0 resolve through their MAP
FID, scenery PRO PID, and FRM identity as follows:

| Logical FRM | FID | PID | Elevation-0 placements | FRM SHA-256 | Stored FPS | Frames per direction |
| --- | --- | --- | ---: | --- | ---: | ---: |
| `art\scenery\atorch3.frm` | `0200056b` | `0200055d` | 6 | `0a1f139428ced37015eaf358cd981910c7a57cc7755db75e213d4345051dd963` | 1 | 1 |
| `art\scenery\atorch4.frm` | `0200056c` | `0200055e` | 10 | `c89b7df15ec2d02feafbacac3deee2b7e97a2aeb86a91f18d1d99f364b83bdf9` | 1 | 1 |
| `art\scenery\atorch5.frm` | `0200056d` | `0200055f` | 6 | `14bed720580ffc960bb436b1e59f398ee1f8b631164605b1a2ae9c3c3acf5dc8` | 1 | 1 |

All three are FRM version 4. Each direction aliases the same single stored
frame. The MAP frame is zero for every retained placement. Consequently,
`floor(elapsed * storedFps) % framesPerDirection` is always zero: a native
runtime cannot truthfully report a torch frame transition from these records.

The three logical FRMs are separate source identities used by different MAP
placements. They are not consecutive frames of one animation. Cycling
`atorch3 -> atorch4 -> atorch5` would change the MAP-owned FID/PID and is
therefore prohibited.

The source FRM pixel streams do use palette indices `243..247`. MAP, PRO, and
FRM provide neither a palette-cycle ordering nor a palette-cycle interval.
Inventing either would violate this task's MAP/PRO/FRM-only boundary. The
current exact decoded source pixels and MAP-bound light fields therefore remain
the only admitted torch presentation.

## Genuine animated fire source is a different object

Map 3 also owns `art\scenery\firepit.frm`, SHA-256
`37d22041dc20a0d24982b8a6542452a7518646042828cac5d3eb7c99db3211a1`.
That FRM contains 12 frames per direction at a stored cadence of 10 FPS. Its
only MAP placement is serial 3471, tile 15128, elevation 2, FID `020004bb`.
It is not one of the 22 retained elevation-0 wall torches and cannot lawfully be
substituted for them. Transporting it belongs to an elevation-2 presentation
slice.

ARVILLAG (Map 4, map SHA-256
`0edcdff2afb6fac7e8203ce9eae8ba4663d37f3be112d3ef4713af3093d8d52a`)
contains zero `atorch*` or `firepit` FRM bindings. Adding a torch animation to
ARVILLAG would therefore be authored content, not source transport.

## Retained state

- The r26 peaceful ARVILLAG branch is unchanged.
- The r29 Temple combat/loot/equip/save branch is unchanged.
- No cache was rebuilt or mutated.
- No smoke, procedural flame, guessed palette cadence, or cross-FID animation
  was added.

The next source-backed implementation can be one of two explicitly different
tasks: transport the real elevation-2 `firepit.frm` 12-frame/10-FPS sequence,
or first obtain an admissible palette-cycle timing/direction contract outside
the present MAP/PRO/FRM-only scope. Neither may be claimed as animation of the
22 retained one-frame torch FRMs until that authority exists.
