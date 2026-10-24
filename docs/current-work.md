# Current work

Updated 2026-10-24. Deliver fresh ordinary New Vegas through Doc, Sunny's wells,
ingredients and campfire, then Primm/ED-E. Recording is off. Owned retail inputs
are read-only. No campaign, physical XR or retail parity completion claim.

## Baseline and ownership

Accepted main is d760340b0ef081b08a231b4856579acd81858fa2 (PR305), merged16:17:14Z
after all five exact-head hosted checks passed. Local main, origin/main and live
remote were verified equal; merge tree matches tested head82f86692. Current
branch is codex/fnv-campfire-progression. Unrelated held PR242/269 stay untouched.
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
No gameplay runtime is active. Any future combat macro must stop its controller,
read fresh authoritative player state and verify pause before returning to
analysis; never blindly toggle Escape after automatic pause.

17:31Z read-only recovery observation: game remains alive/paused, next request170.
Publication failures55 are retained. Last failed publication1840 had an actual
CommitStarted IOException sharing violation0x80070020; publication4464 subsequently
completed in15.714ms. Coordinator's earlier Get-Content -Tail reader lacked delete
sharing and likely caused this burst; it has closed. All current live reads use
FileShare.ReadWrite|Delete. Do not reset counters or classify this reader error as
a gameplay defect. Input remains stopped pending explicit recovery reconciliation.

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
2585 public files, no source/add/protected-save drift. Checked publication is next.

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
change remains. Explicit approval is required for that persistence cut.
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

Twelve-file staged foundation: .staging/classic-animation/classic-foundation.patch,
SHA2561cadea3b663250eb7f39ccaebdb9e0a008a5199deff10b4e275da1db9009fb00.
Public classic files remain unchanged. It connects source actor geometry picking,
request/turn/prefix persistence, source animation commands and committed-elevation
map-update delivery. Hit/action-frame/RNG/AP/ammo/HP/death resolution and NPC
hostility/AI execution remain unbound; combat exit has no completed producer.
No working encounter/fire/campaign claim. Revised Arroyo candidates remain staged
until source silhouettes/seams/passage fit are admitted; no forced mappings.

## Next integrated boundary

Keep the game stopped during the serialized integration window. Finish v2 instrument
source/assets and grouped compile/native interaction/locked-concept comparison.
Continue remaining ordinary wells, ingredients and actual campfire interactions;
implement related proven failures as one repair batch. Keep independent gameplay
publication moving. Run repository-required checks once at the coherent changed
boundary, then checked PR/all five exact-head hosted successes/normal merge.
No repeated optional audit passes, fabricated progress or save-compatibility work.
