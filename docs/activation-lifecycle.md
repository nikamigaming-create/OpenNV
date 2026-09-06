# Source activation and rendered menus

Object activation evaluates the winning SCPT OnActivate program with its own
compiled reference bindings. Quest stage, displayed/completed objectives and
qualified quest variables read authoritative shared state. A false source
condition emits no effects. Unknown functions, reference-instance assignments
and queries following effects fail closed instead of using stale state.

The current presentation adapter validates reached menu and SetStage effects
before invoking them in source order. The Vigor source opens its menu, then
enters its next quest stage immediately. Accepting SPECIAL publishes the
allocation; it no longer introduces an extra, delayed stage transition.
Retained native review telemetry independently shows the next stage while the
menu is still open.

The rendered menu pauses world processing and leases the existing original
menu-background compositor. Acceptance or removal releases the effect and
restores the prior pause state. No world geometry is hidden or replaced.

Synthetic override, predicate, binding and ordering checks pass. Six owned
stage/objective combinations admit only the authored case. Ordinary room-77
presses activate before the objective is displayed: the source program emits
zero effects and Doc's speech continues. The later press emits the menu and
stage effects. Stage 65, actor source time and world-hour bits remain stable
through allocation and review while the original background is visibly blurred.

This is an activation subset, not a general reference-script VM. Reference
variables, trigger/furniture coordination, synchronous query/effect interleaving
and complete MenuMode scheduling remain unbound. Vigor camera framing, the
visible background cabinet, lamp response and matched pixels remain defects.
