The rig is controlled diagnostic evidence, not ordinary campaign acceptance.
The source catalog, runtime action, pose, pixels, audio, state and performance
remain separate lanes. A catalog row or accepted command is never a pass.

`ParityRigRunner.RunAsync(plan, controls, cancellationToken)` accepts exactly
`retail` and `opennv` implementations of `IParityRigEngineControl`. Each adapter
must complete the actual requested owner and return a fresh correlated terminal
receipt. Transport submission alone is insufficient. Both engines save distinct
fresh checkpoints before any case preparation. Preparation and each action run
on both engines concurrently; actual receipt/capture timestamps determine the
join, not the order in which tasks were started. Finally, each attempted engine
is independently quiesced and its confirmed checkpoint restored, even after
cancellation. Unconfirmed saves and failed restoration remain explicit errors.
Quiescence must prevent delayed commands from executing; it must not cancel a
real gameplay transfer by clearing its authoritative state.

The requested target is 3840x2160 at 60 FPS. These values are requirements, not
measurements. The adapter must retain actual viewport and cadence evidence.
Action observation duration is 0.1–8 seconds. Evidence entries require a lane,
path, schema and SHA-256; readers/diff validate their contents independently.
An `observed` case means required artifacts exist, never that their values agree.

OpenNV live-harness commands use the existing sequential request/receipt channel:

* `rig.source`: ordered plugin names/hashes/load order and admitted action kinds.
* `rig.prepare`: `caseId`, `reference`, expected base `sourceForm`, and optional
  `entryReference`. The optional authored entry uses the shared Player MoveTo
  owner with zero offsets. Without it, the actual checkpoint location/camera is
  retained and the target must already be resident and enabled. No actor is
  placed, copied or created.
* `rig.status`: optional matching `caseId`; returns pending/prepared and actual
  source identity, node instance, target/camera matrices, FOV and viewport.
  Prepared does not prove visibility in final pixels or matching source clocks.
* `rig.action`: matching `caseId`, `action`, optional `argument` and
  `initialization`. Admitted actions are `observe`, `object.playgroup`,
  `actor.playidle` (argument is the owned IDLE editor ID), and
  `reference.activate`. They invoke existing real C# owners. Their immediate
  disposition is admitted; the adapter must observe actual action completion.
* `rig.quiesce`: releases harness inputs/bots/captures, then requires actual
  source transfers and admitted activations to settle. It does not clear their
  gameplay continuation to claim success.
* `object.pose`: `reference`, `seconds` (0.1–8). Output is
  `<request>.object-pose.json`. It captures actual starting hierarchy membership,
  transforms, controller clocks, morph values and embedded skeleton bones after
  physics/after draw. Capture stops visibly at retirement/error/32 MiB. Shader
  parameters, GPU skin matrices, audio and final pixels remain independent.
* `actor.pose` retains its existing NPC/CREA all-bone operation.

Rig control outputs are `<request>.rig.json`. The ordinary harness receipt is
only delivery acknowledgement. Adapter requests carry run/request GUIDs and
must correlate those with the concrete engine channel request and artifacts.
The adapter verifies ordered source hashes before preparation and actual
reference/base/camera identity afterward. It must reject a mismatched source.

Arbitrary actor PlayGroup, anonymous actor creation and weapon inventory/equip
setup are currently unbound rig actions. Ordinary player reload/fire input may
use the existing input owner only after the exact equipped source weapon and
checkpoint state are verified. Missing entry markers are not fabricated. Cases
without a legitimate resident reference/entry and action owner stay unbound.

The concrete `OpenNvRigEngineControl` consumes `OpenNvRigConfiguration`: absolute
`HarnessDirectory` and `OutputDirectory`, actual `ProcessId`, ordered
`Plugins` (`Name`, `Sha256`), and a `Bindings` dictionary keyed by case ID.
Each admitted binding names the exact `Reference`, `SourceForm`, `BindingHash`,
`PoseOperation` (`actor.pose`/`object.pose`), optional authored `EntryReference`,
optional exact `IdleForm`, and optional `ExpectedVerticalFov`. No constructor
publishes a game command. The adapter uses the same exclusive
`sequential-input.owner`/`command-publication.lock` protocol as ordinary input.

Compute plan `SourceStackHash` using
`OpenNvRigEngineControl.SourceStackHash(plugins)`. Its canonical domain is
`opennv-animation-rig-source-stack/v1`, followed by each zero-based load index,
lowercase plugin name and lowercase SHA-256, each newline-terminated. This is a
rig manifest join, distinct from campaign save compatibility. Actual ordered
runtime hashes must reproduce it. After restoration they are queried again.

The adapter implements confirmed checkpoint save/load, prepare/status, and
bounded `observe`, `object.playgroup` and `actor.playidle` observations. IDLE
requires the exact catalog-resolved source form. Source activation remains
unbound in this tool adapter until an appropriate completion observable is
specified. Actual action/pose-owner errors remain failed cases with retained
artifacts. Runtime source/base/IDLE identities are verified; the full catalog
binding graph is not recomputed from the live skeleton and is not a parity pass.

Implemented evidence lanes are `source`, `checkpoint`, `camera`, `pose`,
`action`, `performance`, `quiescence`, plus `source-restored` after load.
Camera matrices are retained for the comparison reader's native join; a
configured FOV mismatch or wrong actual viewport fails. Performance is the
actual draw-counter average during capture; target and measured cadence remain
separate. Failed/cancelled commands also get a durable `receipt` journal.
