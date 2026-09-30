# Native reference event contract

The ordinary native player ray and source primitive contacts feed
`FalloutReferenceScripts`, the same C# owner exercised by the development lab.
Reference locals outlive the Godot nodes. The adapter has no quest/stage table.

## Admitted behavior

- Frame events are evaluated in authored block order. Ordinary activation is
  queued into that frame, alongside contact, load and GameMode events.
- An authored OnActivate block suppresses the default action even when its body
  does nothing. An Activate command bypasses that block. Header arguments on
  OnActivate are ignored; action-reference queries filter inside the body.
- Trigger admission adds one entering reference per frame. When no enter is
  pending, it removes one departing reference. Enter and leave do not share a
  frame. OnTrigger uses admitted contact membership without supplying an action
  reference. Physics encounter order is retained; retail tie ordering is unproven.
- Stage-completion queries read entered-stage state, not just the current maximum
  stage. Effects execute synchronously so subsequent script queries see them.
  Reached unsupported commands preserve the executed prefix and fault the owner.
- Message presentation follows the shared consumptive result slot. Superseded
  requests cannot accept input and are removed before display or cold restoration.
  A replacement also dismisses an already visible obsolete canvas. Accepted
  input retains the source button index and can be consumed once by its caller.
  This resolves a runtime ownership contradiction; multiple-message retail
  presentation ordering has not been parity-reviewed.
- XPRM has 32 bytes: three half extents, editor color, an unknown float and a
  shape kind. The native adapter binds boxes and uniform spheres. XTRI is an
  optional uint32 collision layer; the trigger layer is 12. Other shape/filter
  cases fail visibly. Reference scale and transform remain source-owned.

These neutral contracts draw on the published
[activation behavior](https://geckwiki.com/index.php/OnActivate),
[enter behavior](https://geckwiki.com/index.php/OnTriggerEnter),
[leave behavior](https://geckwiki.com/index.php?title=OnTriggerLeave),
[contact block](https://geckwiki.com/index.php/OnTrigger), and
[xEdit FNV format declarations](https://github.com/TES5Edit/TES5Edit/blob/dev-4.1.5/Core/wbDefinitionsFNV.pas).
No external implementation or retail script body is incorporated into OpenNV.

## Saved read failures and world queries

Legacy reference saves retain reached script errors. A missing-read error can
now retry when the same event is admitted and its unchanged source establishes
that the read preceded every mutation. Inspection never invokes a query. Earlier
assignments, rewards, consumptive queries, ambiguous repeated occurrences and
multiple matching blocks prevent recovery. Other failures retain their executed
prefix; they are not globally cleared. Recovery and the next reached failure
are separately reported by the native event owner.

`GetCurrentTime` reads the shared fractional game hour. `GetRandomPercent` uses
one saved stream shared by reference, result and quest scripts, with integer
results from 0 through 99. Its random sequence is an OpenNV policy, not matched
retail randomness. `IsInInterior` and NPC/creature package condition 300 read the
calling reference's current CELL flags. Moved references use their retained
placement; the player supplies the current gameplay cell. A missing player-cell
owner is an error, not an exterior answer. These queries follow the published
[random-percent](https://geckwiki.com/index.php/GetRandomPercent) and
[interior-query](https://geckwiki.com/index.php/IsInInterior) contracts.

Synthetic checks cover pure-read recovery, rejected repeated effects, retained
locals, cold random continuation and distinct moved/player cell state. The
selected genuine checkpoint contains 37 recoverable clock/random failures;
all 37 pass the owned-data recovery audit. A copied ordinary flat Continue
resumes 23 resident instances. Source animation commands/queries and several
actor package procedures still stop execution afterward. Recovery does not
establish that those complete scripts or routines work.

## Managed object animation

PlayGroup resolves a unique authored group in the target reference's NIF
manager. Initialization 0 queues after the current cycle, 1 starts immediately
and 2 starts at the authored loop key. Text keys and cycle boundaries advance
in source order even when a callback selects another group within a long frame.
IsAnimPlaying's optional group selects a manager/type, not an active clip name.
Actor skeleton groups, absent groups and ambiguous managers remain explicit
boundaries.

Save v22 retains the source hash/controller identity, selected clock, consumed
start event and pending group. Earlier supported schemas, including v21, still
load. Cold restoration does not replay consumed keys. Warm eviction retains the
clock, and retiring a presentation cannot detach its replacement's capture.

Recovery clears only the exact old missing-PlayGroup fault when static
inspection proves that the unchanged admitted block reached that command before
any mutation. Pure guards and alternatives of the same failed first command are
admitted; prior rewards, assignments and consumptive reads prevent recovery.
The owned native fixture recovers all 51 selected saved failures, checks six
source script/model families and one alternative-loop window model, and rejects
duplicate rewards after cold continuation. Ordinary exported flat input also
harvests one Coyote Tobacco Chew and saves the harvested source state. These
checks do not establish retail animation or campaign parity.

## Script death and inherited locals

Kill and KillActor with zero or one killer argument use the shared reference
health/death transition. They retain modifier pools and existing limb damage,
grant the source death inventory once and queue the existing delayed OnDeath
event. An omitted killer is unknown: unfiltered death blocks can run, and a
player-filtered block does not acquire a fabricated player identity. Cold saves
retain the delay, loot and consumed event. Calling Kill on an authored corpse
does not create another death or reward. The native combat owner observes fresh
script deaths and activates the same source ragdoll used by combat.

The published [KillActor contract](https://geck.uesp.net/w/index.php?title=KillActor)
also declares limb/cause parameters. Those, essential recovery and player script
death remain explicit unsupported owners; this change does not approximate them.
Exact old first-command Kill/KillActor errors can recover using the same
before-mutation inspection as PlayGroup. Consumptive command arguments also
prevent recovery.

Script local lookup now consumes the world instance's retained script owner.
This includes local and qualified reads on actors whose script comes from a
template rather than direct base SCRI. The source script and save declarations
remain the admission boundary. The owned fixture recovers all 14 selected saved
Kill failures, completes their authored corpse flags/locals, and checks a living
source creature's ragdoll and cold continuation. Synthetic tests cover inherited
and qualified locals, optional killers, filtered death events and conserved loot.

## Executable checks and limits

`ReferenceScriptContractProbe` checks synthetic overrides, activation suppression,
effect/query order, simultaneous contacts, filters, independent locals and faults.
An optional owned installation argument additionally checks Doc's trigger/menu
scripts and the Goodsprings cave's cross-reference trigger, including restored
reference locals. Its effect host does not establish visible menus or gameplay.

`res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn` exercises
real Godot overlap/leave/re-entry, source half extents, axis conversion and queued
activation with disposable synthetic state. The existing full gate includes it.

From the repository root, reproduce the selected checks with:

```powershell
dotnet run --project contract-tests/ReferenceScriptContractProbe -c Release -- 'D:\SteamLibrary\steamapps\common\Fallout New Vegas\Data'
dotnet build runtime/OpenNV.sln -c Debug --nologo
& 'D:\code\gd\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path runtime res://tools/NativeReferenceEventsAudit/NativeReferenceEventsAudit.tscn
```

The ordinary entry points are connected, but the full ordinary opening has not
been replayed after this change. Actual player furniture, posed actor query
contacts, source conversation choices and non-fading reference enable now have
component checks; see dialogue-and-furniture.md. Complete actor/creature physics,
native fades, deletion, MenuMode event admission and cold interaction/procedure
restoration remain incomplete. No matched retail timing, collision-filter,
presentation or parity claim follows.
