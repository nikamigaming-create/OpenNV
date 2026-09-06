# Furniture approach and entry

A later NPC furniture package now travels to its entry point and plays the
winning entry IDLE/KF before reporting occupation and package completion.
Initial process binding retains the existing source furniture placement and
identifies that disposition separately in telemetry.

The FURN mask chooses the enabled NIF furniture marker. Its source placement
and heading define the occupied root and approach frame. Entry accumulation
is anchored by the authored terminal root; the start of that same curve gives
the navigation destination. NAVM supplies the corridor. If its projected
endpoint differs in height from the authored entry point, the path retains
both points rather than snapping to the furniture on arrival. The actor then
plays the finite source entry and switches to the occupied loop. Exit uses
the source curve in the same approach frame.

IDLE conditions choose the animation, including the winning race's Child flag.
Race properties use the documented RACE.DATA layout; private observation of
the owned command descriptor identifies the IsChild condition. No actor,
location, furniture name or animation filename selects this transition.
The entry's native script-visible procedure code is not yet observed: telemetry
publishes it as unknown, and an entry predicate requiring it fails closed.
Unknown marker selection, root rotation/scale, ANIO or clock behavior remains
an explicit failure.

Navigation telemetry now includes its package, target reference and purpose.
The actor publishes its actual world position and orientation. A retained
path from an earlier package cannot be presented as the current route.
The earlier room-81 couch-route report was incorrect: that path belonged to
the previous package, while the furniture package used direct placement.

The synthetic placement probe checks translated/rotated/scaled approach
frames, entry/exit endpoints, malformed roots and race-data extents. The
selected owned audit exercises exit, navigation, entry, occupation and exactly
one completion. It rejects completion during approach or entry, stale path
provenance and a root jump exceeding 25 cm in a 60 Hz audit frame. The selected
case records 985 approach frames and 104 entry frames, with a maximum root
step of 0.02550 metres. This is a component audit, not native timing or pixels.

```powershell
& $Godot --headless --path runtime res://tools/NativeActorPerformanceAudit/NativeActorPerformanceAudit.tscn -- --furniture $DataRoot $CellHex $ActorReferenceHex $QuestEditorId $Stage
```

Player furniture activation, temporary third-person presentation, reference
script state, trigger scheduling and original conversation choices remain
unbound. A private ordinary retail capture shows the couch entry switching to
third person, returning to first person, then starting the questionnaire.
Native navigation costs, collision/avoidance, animation blending, sounds,
complete transition timing, occupation persistence and matched final pixels
remain separate open requirements.
