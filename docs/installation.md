# Installation status

OpenNV is replacing its historical runtime preview with a first-party Godot
runtime. The checked-in Windows slice is a playable experimental Goodsprings
sandbox; no full-campaign package is published yet.

Developers can validate the asset-free synthetic slice on Windows with Godot
4.7.2 Mono, .NET 9, and Python 3.11.9:

```powershell
python -m pip install -r content/requirements.txt
.\scripts\Test-GodotRuntime.ps1
```

To start the source launcher against the checked-in runtime on this computer,
install the launcher's dependencies once with `npm install` from `desktop`, then
run:

```powershell
.\scripts\Start-OpenNV.ps1
```

The script finds the local Godot 4.7.2 Mono build, sets
`OPENNV_RUNTIME_ROOT` to the repository runtime, and sets `OPENNV_GODOT` before
starting Electron. On another checkout, supply Godot explicitly:

```powershell
.\scripts\Start-OpenNV.ps1 -Godot 'C:\Path\To\Godot_v4.7.2-stable_mono_win64_console.exe'
```

This start command neither reads retail data nor creates a content cache. Game
inputs remain selected or registered inside the launcher.

An optional owned-data check starts from the player's legal game installation.
It hashes the master and meshes archive, extracts the model directly, prepares a
temporary cache, loads it in Godot, and removes the cache afterward:

```powershell
.\scripts\Test-GodotRuntime.ps1 `
  -FalloutNewVegasData 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
```

Experimental packaged builds include `OpenNV.Content.exe` beside the Godot
runtime. A player launches `OpenNV.exe`, selects either their legal Fallout: New
Vegas installation folder or its `Data` folder, and the runtime prepares its
private cache directly. The player
does not install Python or another engine. The retail input is read-only, and no
commercial file or conversion output is committed or distributed. The verified
cache remembers the selected installation and is reopened automatically on
later launches. The separate saloon diagnostic opens the Goodsprings Prospector
Saloon at the main entrance's data-defined XTEL target. In that component, use WASD/mouse, E to
collect/activate, hold Z while aiming at movable clutter and release to drop it,
left-click to fire the initially equipped owned-data 10mm, R to
reload, and F5 to save.
Aim at the intact pool table or one of its balls and press E to enter practice
mode. Left-click strikes along the camera heading, the mouse wheel changes the
configured power, R resets every ball to its authored Saloon transform, and E
or Escape returns to the weapon. In OpenXR, grip enters/exits, hold trigger and
sweep the tracked cue through the cue ball, and B resets the table. This pool
experiment is currently unsupported: its native flat contact gate fails because
the ruined-table retail ball placements do not form a playable layout on the
intact replacement, so pocket/save/reset and OpenXR behavior are not accepted.

The launcher shows four top-level game choices: Fallout 1, Fallout 2, New Vegas,
and Fallout 3. Every card shows the same FPS/Hex/VR mode row; unsupported modes
remain visible and disabled. The current manifest admits Fallout 1 Hex/FPS,
Fallout 2 Hex, and New Vegas FPS plus an experimental OpenXR route under their
separate runtime gates. Fallout 1 VR, Fallout 2 FPS/VR, New Vegas Hex, and all
Fallout 3 presentations remain disabled. TTW is not a fifth game button:
TTW-FO3 is an edition under Fallout 3 and TTW-FNV is an edition under New Vegas.
Both TTW editions remain disabled. On
this development machine Fallout 1's generated V13ENT
inputs and Fallout 3's owned GOTY profile are registered. New Vegas launches the
owned menu, skippable intro, and Doc Mitchell route from the verified local
cache. The launcher supplies a profile-owned Courier save path; the runtime's
`user://saves/new-vegas-opening-v1.json` is only the direct-launch fallback.
That campaign save remains separate from the older Goodsprings sandbox save. After the opening completes,
its source-bound HUD/Pip-Boy runtime shell
reads the same inventory, quest, map, and save state; complete tile interaction
and retail UI parity remain pending. STATS currently shares the verified ITEMS
frame while its remaining Gamebryo layout expressions are unsupported. The
default cache eagerly instantiates the ordered Doc house, Goodsprings active
exterior set, and saloon composite with both reciprocal XTEL pairs. From a
completed stage-200 save, configured flat input traverses both forward XTEL
links; campaign save v6 records source-derived Level/HP/AP/XP plus saloon CELL
`00106185`, and a fresh owned-menu Continue restores the unchanged save and
player transform there. A source-portal active set suspends distant resources,
and the current r25 first-run/cold-Continue pair passes against the admitted
four-family cache with exact selected source-door identity. The active-CELL
WorldEnvironment/sky owner now restores interior XCLL background/fog and renders the owned
configured clear-day `NVWastelandClear` atmosphere/cloud pair outside without
rebuilding that cache. Exterior surface/directional lighting remains
provisional. Dynamic time/weather, reverse traversal, neighboring exterior-grid
streaming, integrated-route OpenXR acceptance, and visual parity remain
pending. The registered Fallout 3 development frontend opens its profile-backed
menu, plays a locally converted and hash-verified copy of the owned intro, and
converges through Escape or Skip on the same CG00 character state. Its current
early-birth implementation starts admitted actors from source `PACK` sections,
selects hash-bound KF sequences, and composes the sampled `Camera1st` skeleton
node through its source parent chain without applying a `NiCamera` axis fix.
That correction does not enable the launcher route. The current toddler proof
auto-steers toward its target; ordinary configured user input, physical trigger
entry, source actor/camera timing, and a matched retail/native differential are
still required. The current creator preview remains an owned-texture inspection
surface rather than a 3D FaceGen actor. Fallout 1 OpenXR has a shared-state V13ENT simulator adapter that
passes locomotion, snap turn, fire, reload, and save. XR door use,
campaign-native hands/weapon/UI, launcher enablement, and physical-headset
acceptance remain unpromoted. TTW currently consumes effective records only;
resource winners are not connected to a playable TTW world, and TTW-FNV has no
effective-stack Doc runtime. OpenNV also does not load xNVSE/JAM native DLLs or
implement their complete portable script/event/UI/AP/animation/audio/cosave
surface. Fallout 3 world play and every complete campaign remain unpromoted.

The legally owned Fallout 2 install can be registered without producing a
content cache:

```powershell
.\scripts\Register-OpenNVFallout2.ps1 `
  -Fallout2Root 'D:\SteamLibrary\steamapps\common\Fallout 2'
```

This validates and hashes `master.dat`, `critter.dat`, and `patch000.dat`, plus
their DAT2 directory identities. It does not copy any member. The registered
profile can be passed to `content/tools/fo2_first_slice.py` to emit an
asset-free, hash-bound Temple MAP/PRO/FRM source manifest. Registration alone
does not enable play; the launcher admits the bounded Hex route only when its
matching Temple, transition, Arroyo, player, and character-start artifacts are
all present and hash-valid. FPS and VR remain disabled.

`content/tools/prepare_fo2_temple_presentation.py` can then decode only the
source manifest's admitted tile and object frames into a disposable local PNG
cache. The cache includes a provenance manifest, remains derived owned content,
must not be distributed, and does not change runtime readiness.
The bounded runtime verifies that cache and constructs the admitted Map 3/Map
126 Hex slice. Its player uses a fail-closed true 3D presentation path from an
admitted owned FNV full-body donor over authoritative classic GCD/FRM identity;
there is no procedural, FRM-player, silhouette, or standee fallback, and the
donor is not a parity claim. The Elder movie's normal end
and Skip converge through the same exact terminal source frame/fade and live
camera handoff. Torch anchors use exact owned opaque FRM emitter pixels/centroid
and source MAP light placement, but the admitted emitter is static and does not
transport source flame animation or smoke. The live handoff still requires
visual acceptance. The retained current checkpoints are alternative peaceful
Cameron-to-ARVILLAG and Temple guardian-combat/Spear-loot/equip saves; they do
not merge or imply a dead-guardian exit. Their hashes and exact boundaries are
in the [canonical FO2 branch ledger](evidence/fo2-first-slice-branch-ledger.md).
Full campaign/script coverage remains absent.

The registered Fallout 1 route now opens an asset-free original-style menu;
**New Game** enters the owned character picker and skippable owned Overseer
movie before releasing the selected Hex/FPS view in V13ENT. Its original retail
startup logos and exact retail main-menu presentation are not implemented.
The selected premade/custom identity and sex are wired through cold restore into
the gameplay actor. Max/Albert may use an owned FNV full-body donor only as a
presentation adapter; Natalia/custom geometry is an explicitly first-party
procedural non-parity path. Exact GCD/FRM identity remains authoritative, and a
fresh producer/native acceptance of that integration is still required.
The launcher also shows a diagnostic OpenXR choice but keeps it disabled.
Meta/Oculus Touch and
the OpenXR generic-controller fallback are declared. A repo-local simulator
passes two retail hands, both sticks, locomotion, snap turn, door/fire/reload/
save actions, and native stereo capture. Physical-headset final-eye validation
is still pending and the flat path remains the default.
