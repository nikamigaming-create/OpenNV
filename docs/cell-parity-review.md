# Cell parity review

The user-directed next work is a matched retail/OpenNV pass through every cell,
with failures repaired in shared source readers and runtime owners. Start with
the saved Goodsprings exterior, its adjoining cells and connected interiors,
then follow the source world and door graph toward Novac and the Strip. Keep all
winning installed cells in the queue, including empty cells, persistent-reference
storage cells and DLC. The wider NV/FO3/TTW objective remains open.

## Comparison and repair loop

1. Identify the winning plugin/resource stack and retail executable build.
   Observe the admitted retail plugin array: FNV NAM sidecars activate plugins
   that need not appear in plugins.txt. Compare that array with the native audit:
   `dotnet run --project contract-tests/FalloutPluginRuntimeProbe -- --audit-load-order <owned-data>`.
   Match the source CELL, player/save branch, time/weather, camera, actor and
   equipment state before evaluating a pair. Record deliberate VR adaptations.
2. Use ordinary gameplay in OpenNV and the read-only Win32 retail observer.
   Retail measurements never become OpenNV gameplay state. A source-only audit
   can reveal an implementation failure while a retail pair is unavailable;
   it cannot close a matched comparison.
   The user has authorized the separate private native input bridge to move the
   retail player and create comparison saves. Start from a copied checkpoint,
   use distinct save names, verify original save hashes and release held input
   before leaving a paused session. Manual user loading is no longer a dependency.
3. Capture the actual resident source set and runtime failures. Preserve source
   ancestry: an exterior reference can reside in the nearby grid while belonging
   to the world's persistent storage cell. Group repeated failures by the owner
   and source behavior they share, retaining every affected reference.
4. Review geometry, placement, materials, lighting, sky/LOD, actors and motion,
   effects, sound, UI, interactions and persistent gameplay against matched
   retail evidence. Unobserved lanes remain unverified. A reference-presence
   count or a screenshot cannot establish complete cell correctness.
5. Fix the earliest incorrect shared owner. Use synthetic changes and affected
   owned examples to reject location-specific behavior, then revisit affected
   cells and cold-load their state. Repeat flat before SIM; physical XR remains
   held until the requested real combat/death/loot sequence works.

Record a failure's source identity, symptom, evidence lane, owner, affected
cells, uncertainty and closing check. Unknown layouts, missing objects, absent
events/audio and telemetry loss remain failures. Keep recording off except for
a selected visual check; remove temporary frames after inspection.

## Existing commands

The reference lifecycle sweep now emits v2 rows with stable cell identity,
worldspace/grid, plugin hashes, failures and cold-state results:

```powershell
dotnet run --project tools/OpenNV.DevelopmentLab -- lifecycle <owned-data> --all
```

For an ordinary loaded cell, send the existing native live-harness `state`
command. Its detailed snapshot includes the exact resident reference set,
source compatibility identity, loaded assembly identity and capture time.
Then run:

```powershell
dotnet run --project tools/OpenNV.DevelopmentLab -- cell-review <owned-data> <detailed-state.json>
```

The report groups missing runtime observations and declared actor/reference
failures, resolves source ancestry and models, and counts excluded diagnostics
from nonresident references. A mismatched source stack, duplicate scope or
missing reference outside that scope rejects the report. Its parity status is
always unverified; it is a failure list for the paired review, not a pass gate
for pixels, audio or gameplay. Retain matched evidence separately through the
existing parity protocol in [parity-telemetry.md](parity-telemetry.md).

## Current verified corrections

The active source loader admits explicit plugins and FNV NAM activation, orders
them by master flag/timestamps and excludes inactive archives. NAM activation
is independently documented by [libloadorder](https://github.com/Ortham/libloadorder/blob/master/src/game_settings.rs).
The observed retail array and native default both contain the same ten official
plugins; their native save compatibility identity is unchanged. Synthetic
activation/encoding/order/archive cases pass. This does not certify every mod
dependency, INI override, resource precedence rule or gameplay behavior.

Unused environment-mask metadata previously rejected the entire gas-station
SCOL. The shared material owner now ignores the dormant mask while retaining
requirements for an active environment path. Synthetic mask variations and the
owned eight-surface model pass, and the ordinary exterior shows the building.
The starting cell's gas-station presence/material failures are gone; remaining
foliage, controller, actor and LOD failures remain open.

The diagnostic command reader also preserves a request across an I/O lock,
reports the pending failure and advances only after a complete read. A deliberate
lock in the actual native runtime kept physics running and delivered the same
state request once after release. Recording remains off outside selected checks.

The shared SLSD reader compared unused compiler bytes when checking duplicate
local-variable declarations. The [documented script layout](https://tes5edit.github.io/fopdoc/FalloutNV/Records/Subrecords/Script.html)
defines the slot, one flag byte and unused padding. Seven owned scripts repeated
the same slot/name/flags with different padding. The reader now compares those
semantic fields while the source hash still binds every original byte. Different
names, flags and conflicting name-to-slot mappings remain rejected.

The corrected owned sweep loads all 44,517 winning CELL records, including
5,870 with references, and passes repeated teardown and cold state restoration.
The previously failing persistent world cell owns 6,093 references, 919 scripted
references and 326 distinct attached scripts. All source script declarations
now pass their reader. These counts establish this bounded lifecycle correction;
every cell's complete retail comparison remains open.

Private current results are in `tmp/development-lab/cell-sweep-lifecycle.json`
and `cell-sweep-script-contracts.log`. Runtime snapshots and grouped reports
remain private. See [current-work.md](current-work.md) for the latest ordinary
checkpoint and the current source-stack/state alignment still needed.
