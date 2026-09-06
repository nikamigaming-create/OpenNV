# Head tracking

Script targets and a physical head-pose owner now bind the selected source
declarations. This does not establish complete actor aiming, eye movement,
native frame correspondence or the couch questionnaire.

## Owned declarations

Float defaults retain the compiler's value/name/descriptor relationship in
both framed and optimized initializers. Optimized x87 constant loads encode
their zero/unit values directly. Values are never substituted by setting name.
Existing INI precedence remains authoritative; missing or unrecognized defaults
still fail. The five LookIK distance, angular-step and easing declarations now
read through this path.

The BPTD reader selects parts with both IK-data and head-tracking flags. BPNT
declares the tracking node; BPNN has a different role. The admitted BPND extent
retains its body-part slot, flags and degree-valued cone. Selection follows the
body-part table's slot order. A display name is optional, so BPTN cannot be a
mandatory part delimiter. Missing selected target names, invalid extents,
repeated/out-of-range slots and non-finite limits remain errors. Combat,
dismemberment and other body-part behavior are outside this reader's scope.

## Evidence and limits

Synthetic tests vary target names, cones, part ordering, optional display names,
source scalar bits and initializer layouts. They reject malformed, truncated,
ambiguous and non-finite declarations. The owned audit reads all 58 loaded BPTD
records: 15 select tracking declarations and 43 do not. These are declaration
counts only.

Private Win32 observe-mode measurements agree with all five selected setting
bits, the selected humanoid body-part slot, BPNT node and cone bits. Retail
addresses, code, records and captures remain private. The full runtime gate
passes with these readers. Declaration agreement does not prove a pose/frame
match.

## Script and pose owners

Look/StopLook bind actor and target references from their own compiled scope.
Package events, quest-stage results and INFO end results use the same target
owner. A matching global EDID does not grant access. Conditional/looping result
programs and the nonzero whole-body Look mode remain unsupported. StopLook has
no declared arguments; a trailing reference spelling does not become a target.

Six target slots retain independent references and enabled flags. Script Look
sets priority two; higher active priorities win. StopLook clears that slot,
copies its former target into the default slot without changing the default
enabled flag, and starts the winning hold timer. The cached selection and
current selection are separate. Float32 countdown overshoot is preserved;
expiry does not invent automatic acquisition or erase the stored reference.

The ordinary NPC bootstrap selects the winning humanoid BPTD. Its tracking
bone and parent provide rest-space forward axes directly from the owned NIF.
The post-animation owner clamps the target direction to the authored cone,
applies the source angular step per publication, and eases back to the authored
pose when tracking releases. Restoring that pose before KF evaluation prevents
procedural rotation from accumulating into animation. The declared float
extra-data controller supplies the override channel; values at or above the
engine's 90 threshold suppress tracking. Neither the node nor float-property
name is selected by actor or animation identity.

The player's real first-person camera supplies its target point. Other loaded
humanoids expose their source head position. Unsupported target-point owners
remain errors. A reference outside the loaded cell has no active actor process
to receive Look; this disposition is recorded, without constructing an actor.
A missing actor in its loaded source cell still fails. Low-process persistence,
dynamic cell relocation and complete process-level transitions remain open.
Native observation finds both absent processes and a background process among
the introductory references outside the room. OpenNV's unloaded disposition
describes its current owner inventory; it does not prove native process absence.
The separate reference extra-target bookkeeping is also not yet owned. These
remain explicit state-lane gaps, beyond the loaded actor's head-slot/pose owner.

Synthetic contracts cover target priority, invalidation, StopLook flag/cache
lifetime, timer recurrence, script syntax and float-controller declarations.
Native Godot checks cover physical rotation, cone clamping, publication limits,
animation restoration, override suppression and distance gating. The selected
owned audit binds seven opening quest commands and the package command, checks
dormant versus missing loaded actors, and exercises the real skeleton. Private
native observation independently confirms the bootstrap, first-person target,
override threshold and rest axes; axis agreement is within floating-point
rounding, not an exact pose/frame claim.

Automatic/default acquisition, combat targets, eye aiming, whole-body Look,
actor save restoration and matched native pose/timing remain unbound. No
complete scene or questionnaire parity follows from these component checks.

Ordinary room-81 skips the cinematic, accepts original creation and Vigor,
executes Look during the later speech/approach, completes the package Look and
subsequent StopLook, then traverses the eight-waypoint route to the living room.
Doc is seated there at stage 80 without script, animation or package errors.
The bounded timeline retains active head publication, authored suppression and
release; exact retail head motion was not aligned or visually accepted.

Run the selected source audit with the local owned data root:

```powershell
dotnet run --project contract-tests/FalloutDialogueProbe -c Release -- --audit-look-sources $DataRoot
```

The native instance audit also accepts `--look $DataRoot $ActorReferenceHex
$CommaSeparatedQuestEditorIds` for source command binding and physical-bone
checks. This audit uses synthetic target movement, not ordinary gameplay proof.
