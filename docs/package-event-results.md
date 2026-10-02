# Package result execution

NPC package events use the existing C# statement interpreter for their authored
result scripts. `FalloutReferenceScripts.ExecutePackageEvent` validates the
event's compiled extents, binds only its own SCRO/SCRV fields and executes with
the actual placed actor as caller. Unqualified locals and actor commands belong
to that actor; qualified quest/reference names retain compiled source identity.
Conditions, source SetStage and other admitted commands share the same owners as
reference, dialogue and quest results. No quest outcome is substituted.

`RuntimeNativeNpc` runs the result before the event's topic and IDLE. The existing
package lifecycle retains successful prefixes and latches a reached failure;
repeated completion or change requests cannot replay a failed event. Nonzero
event topics still fail after their preceding script. Godot routes results to
the active gameplay executor. During initial world construction, before that
executor exists, the existing bounded head-tracking path remains available;
other early construction effects remain unbound. Actor package cold lifecycle,
blocking result continuation and matched retail scheduling remain incomplete.

Synthetic checks cover master-adjusted references, conditional execution,
actor locals/GetSelf/user values, stage effects, independent event scopes,
exactly-once completion, retained failure prefixes, malformed metadata and
nonactor refusal. Result values survive the existing world snapshot. This does
not establish complete cold package-event restoration.

The selected owned native fixture constructs the actual toddler Dad and his
winning package at CG01 stage 14. Source NAVM/KF travel reaches the actual marker;
the ordinary actor owner admits its On End program and emits exactly one stage
16 result. Source bytes remain unchanged and frame recording stays off. The
fixture enters its initial stage explicitly and does not execute the destination
QSDT program, so it is component evidence rather than campaign progress.

Ordinary input separately opened the source playpen gate, walked through it,
fired the reach-Dad trigger and reached stages 12/14. The prior On End failure
was `setstage CG01 16`. The resumed ordinary route now enters stage 16 and
starts its authored speech, then stops at the missing gate SetOpenState owner.
Further ordinary toddler/Vault progress, gurney travel,
HUD visibility, audio timing, campaign/mod completeness and XR remain open.
