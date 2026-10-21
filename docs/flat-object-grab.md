# Flat object grab

The playable native player now consumes the mapped Grab action (default Z).
It selects the aimed owned NIF dynamic body at ordinary activation reach,
checks the winning `fGrabMaxWeightWalking` or `fGrabMaxWeightRunning`, and keeps
the picked surface at its original depth. Static, frozen, disabled, overweight
and already-owned bodies cannot be acquired. Menus, control disablement,
remapping, teleport and retirement release the hold.

The body integrates a bounded, damped translational force on the physics clock.
It retains its source mass, collision shapes, layers, friction and restitution.
Continuous collision detection is enabled during the hold and restored on
release. Response frequency and maximum acceleration are explicit Godot adapter
policy in the runtime configuration; they are not measured retail parameters.
No transform teleport, collision removal, inventory grant or quest shortcut
implements the hold.

The owned-skull fixture exercises real input and NIF physics with a synthetic
floor and obstacle. It checks motion, picked-surface convergence, release under
gravity, refusal cases and owner retirement. Run it with:

```powershell
& $Godot --headless --path runtime res://tools/NativePlayerPresentationAudit/NativePlayerPresentationAudit.tscn -- --owned-grab $DataRoot
```

Ordinary room placement, all prop families, theft/crime consequences, physical
state save/cold continuation and retail interaction parity remain open.
