# Current work

Updated 2026-10-24. Deliver fresh ordinary New Vegas through Doc, Sunny's wells,
ingredients and campfire, then Primm/ED-E. Recording is off. Owned retail inputs
are read-only. No campaign, physical XR or retail parity completion claim.

## Baseline and ownership

Accepted main is633777651029fd561da8833e2ec85d7e9d584e2f (PR306), merged18:27:44Z
after all five checks passed on exact10efd4bd. Local main, origin/main and live
remote were verified equal and clean; merge tree matches the tested head. Current
branch is codex/fnv-campfire-followthrough; only this owner update is dirty.
Unrelated held PR242/269 stay untouched.
One coordinator owns gameplay input, builds, Git and this document. No competing
build/runtime is authorized. Denied optional old-writer messages are not retried.

## Durable fresh gameplay

The user reset the active profile13:13:15Z: canonical save and30 old slots were
reversibly moved outside the active scan to local/opennv-save-reset-20261024T131315Z.
Never restore those files. Fresh character CourierCourier completed Doc200,
SPECIAL, Guns/Repair/Speech tags, no traits and normal farewell. Ordinary saloon
entry, Sunny dialogue, outdoor rifle/reload and bottle hits completed shooting.
No authored quest, survival, placement or collision state was forced.

Actual first-well rifle contacts killed10a1fd/10a1fe; source death scripts advanced
VCG02 to50. Corpse loot supplied Gecko Meat1. F5 c96b2ea991424621b434955d5f1dbd1e
retains schema61/120HP/both deaths and completed death dispatch. Save/cleanup and
publication errors were absent. Current durable near-next-well checkpoint is
689d4aeb00e94fa197604b7cda11d75a:120HP/Meat1/stage50, player near[-939.55,106.72,199.90],
Sunny alive and the real next-well choice accepted. Later-well kills are not yet
durable; normal Load recovered three ordinary combat deaths. One was an operator
pause-toggle error, not an engine defect. Do not repeat opening acceptance.

Smoke33 normally quit through observed Quit/YES requests172/173; runner1027
returned exit0 and PID3752 is absent. The earned689 checkpoint remains intact.
Sole smoke34 PID20112 / runner39551 continued normally from earned689. Actual
metal-armor/rifle equipment and reload preparation saved through F5 as
c3d01cd3a58b4e0c8046bda4c969b7b6: schema61/120HP/no pending owners or save/cleanup
errors. Ordinary movement then reached[-943.92,106.21,212.67]. Real rifle contacts
killed hostile10abb0. F5 committed0cc85a6a0695441a89231b109c310057 at18:47:55.9412347Z:
schema61/101HP/no pending owners or save/cleanup errors. Immutable saved actor has
Injury.Dead=true, DeathEventPending=false and DeathCount1. Current player healed to71HP through two actual Stimpaks; publication failures0.
Smoke34 normally quit through observed requests98/99; runner39551 returned exit0.
No live game remains. Latest durable0cc85a6a retains101HP/stray10abb0 death. Required well targets10a20f/10a210 remain
unfinished. Any combat/movement macro must stop its controller,
read fresh authoritative player state and verify pause before returning to
analysis; never blindly toggle Escape after automatic pause.

Smoke33's55 sharing failures remain retained; the incompatible coordinator reader
closed and normal Quit/exit0 completed. Current smoke34 failures0. All live reads
use FileShare.ReadWrite|Delete. Do not reset counters or classify reader errors as
gameplay defects. The current bounded operator helper always verifies pause.

## Current implementation boundary

PR305 delivered source control8 Sneak activity/save retention and matching Left
modifier input leases, plus functional original physical-launcher scaffolding.
Its exact-tree required gate passed16:02:57.9800863Z, Gate0/Diff0/Public2542 with
no source/add/protected-save drift; all hosted checks passed before merge.
Sneak camera/capsule/body posture and physical XR remain unbound.

The owner rejected the generic box and browser UI embedded in its CRT as final
art. Historical functional acceptance is not acceptance of that final design.
The four-file hardware-first handoff was hash-checked against exact public bases
and applied: dedicated CRT instrument readout; separate F2 accessible library;
shared semantic game/mode selection; LOAD/CONFIG/RETURN/POWER, brightness,
Master-bus audio and read-view controls. Construction/package identity now names
the instrument. The existing native audit was adapted to these real semantics,
including noninteractive CRT output and filter-independent physical selection.
The revised original body/contract is installed, with named aliases, verified
pivots/curved CRT UVs and metadata-driven camera/brightness/switch poses. Combined
Debug build passed zero warnings/errors; Godot import and native1280x800 preview
passed. Actual CRT
has dedicated output; no regular browser is embedded. Source contract JSON is
explicitly included in exports. Superseded v1 texture/sidecars were removed using
the manifest's exact list. Native comparison found overbright trim, weak text
hierarchy and simplified wear/shoulders/feet. Grouped lighting/readout repairs
compiled with zero warnings/errors. Original v2.1 corrects the four game labels
and keyed90-degree pointer/pin positions while preserving non-dial geometry.
The native interaction run passed: four actual game choices, selector movement
and roundtrip (the production contract validates all four target/pin poses),
five CRT production ray/UV joins, brightness, Master mute, read camera, cancellation,
source picker, separate F2 filter and normal New Vegas launch request. No gameplay
launched. Physical POWER produced exit0 before the audit's failure timeout, with
no automatic-quit fallback. Whole/read/F2 selected native images and input/power
receipts are under .staging/launcher-device-v21/qa. Actual pixels were compared to
locked concept libfile_bd5bdd391cc48191bb230d4932cc4226; wear/shoulders/feet are still
simpler, so no final1:1 claim. The original editable blend ships; supplementary
authoring recipes remain in the original private Library bundles.
Required frozen-tree publication gate passed18:11:19.9395447Z: Gate0/Diff0,
2585 public files, no source/add/protected-save drift. PR306 is accepted on main.

Worker01a16e13 owns Device.cs v2 geometry/control/camera reconciliation, ONLY
SyncInstrumentHardware in Instruments.cs for metadata-driven poses, and
original runtime/assets/launcher/device + art/launcher/device exports/manifest.
Coordinator owns Instruments/Library/main hook/Actions/package/audit integration.
Worker handoff is complete/frozen; all files now belong to coordinator integration.
No worker builds/runtime/Git. Automatic Library transfer already works through
the dedicated Ubuntu environment; do not retry the old Windows helper.

## Remaining gameplay systems

PR304 supplies basic source NPC dart emission/damage and process continuation;
exact aim/visual/RNG parity is unclaimed. Same-region dialogue keeps native floor
and route execution with different interior/world identities isolated.
Doc PACK10b756 current-location Sandbox still needs a retained package-start
anchor. Automatic approval review rejected its connected
persistence patch because schema change/v61 admission conflicted with the stated
no-old-save-compatibility constraint. No denied operation was retried. The prior
partial anchor hunks were inspected and selectively reversed; no anchor or schema
change remains. The parent later forwarded current approval for this exact change without old-save
migration. The normal retry of the staged anchor patch was rejected again:
review did not accept the forwarded transcript as trusted authorization. No anchor
patch executed, including in staging; only unchanged base copies exist there.
Do not retry the same evidence or bypass review. The exact authorization remains
an external blocker, not a reason to retain character progress as acceptance.
Independent source-flag support is now implemented without snapshot/schema edits:
healthy native NPC Sandbox retains its active package while the player is in the
same actual cell; an explicit evaluation or leaving that cell allows reevaluation.
Failed procedures do not gain a hold. Creature continuing-selection remains
explicitly refused. The existing Sandbox probe has flag/cell/explicit-evaluation
coverage and passes; combined build has zero warnings/errors. The selected owned
record audit confirms Doc's package declares this flag alongside unsupported type2.
Current-location PLDT type2 is still refused, so Doc's whole package is not claimed.
Actor Eat requires actor-owned inventory, effects and
persistent procedure state; player ingestibles must not be reused and the source
candidate refusal must not be suppressed. Existing shared campfire recipe and
inventory rollback are implemented, but fresh ingredient/cooking/quest acceptance
remains open. Ordinary combat deaths/controller limits alone justify no engine fix.

## Classic lane

The exact nineteen-file handoff at .staging/classic-combat-execution was applied
with all public-base and staged-next hashes checked, together with the two
optional visual placement loader files. Combined Debug solution build passed
zero warnings/errors. Owned FO2 Narg/Mingan/Chitsa start/drop/cold/pickup/source-
change checks and FO1/FO2 door interaction regressions pass. These are fixture
checks, not native combat acceptance. The existing owned FO2 ARCAVES probe passes
source door clocks/collision and cold/mid-animation restore; scripted lock/use
opcodes remain explicitly unbound. Conditional nonlethal punch, source action
frames, shared clock/RNG, scenery events and retained prefixes are connected.
Armed firing, lethal consequences, enemy turns/AI, additional participants,
critical/status/light and completed combat exit remain explicit boundaries;
no native working-combat/fire/campaign claim.

Arroyo v5.1 config/assets remain staged. Automatic approval review rejected
copying them into public asset/config paths before native review; that copy did
not execute and is not retried. The existing world audit now accepts FO2 and
explicit candidate recipe/model-root inputs, using the same production loader
without promoting held files. Native assembly bound all9candidate pieces with zero unresolved objects. The
existing audit still fails its required joined cave shell assertion in the exterior;
review rejected weakening it. That failure stays visible. Candidate capture is
ordered before the unchanged assertion for inspection, not an audit-pass claim.
Actual Godot pixels were inspected: all9pieces bind but the assembly is dark;
asset/config admission and final art acceptance remain held. No public assets
or candidate config were copied. The selected native review remains exit1.

Required source publication runtime gate finished19:37:56.0714103Z with Gate0/Diff0.
Its raw-byte guard found22launcher importer files rewritten LF-only by Godot.
Every CRLF reconstruction matched the exact pre-gate SHA before writing; those
original bytes were restored and all2589public hashes matched, no added files or
protected-save drift. The original wrapper exit1 remains recorded alongside the
separate importer reconciliation; no semantic or runtime source changed.
Checked source publication is next; no Arroyo asset/config admission.

## Next integrated boundary

Keep the game stopped during the serialized classic integration window.
PR306 already publishes the functional instrument; final visual refinement stays open.
Continue remaining ordinary wells, ingredients and actual campfire interactions;
implement related proven failures as one repair batch. Keep independent gameplay
publication moving. Run repository-required checks once at the coherent changed
boundary, then checked PR/all five exact-head hosted successes/normal merge.
No repeated optional audit passes, fabricated progress or save-compatibility work.
