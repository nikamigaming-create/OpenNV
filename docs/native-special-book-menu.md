# Owned SPECIAL book menu

The SPECIAL book presentation reads the winning `specialbookmenu.xml`,
`Meshes/Terminals/Babybook02.NIF`, its eighteen controller sequences, its DDS
materials and winning menu SOUN forms. The executable declaration reader binds
the model resource, scale, axis rotation, depth, projection factors, conditional
fallback light, default allocation total and ordered forward/backward tables.
It executes no owned native code and retains no extracted launch asset.

The XML is an invisible input protocol for a 3D menu. It declares eight proxy
IDs: next, previous, increase, decrease, Done, pointer, index up and index down.
There is no Reset action. The book starts on page 0 at the first source pose.
Next/previous input traverses pages 0 through 9, including the index on page 8
and the back cover on page 9. The nine forward and nine reverse sequences remain
distinct. The back cover has no registered pointer callback; reverse shoulder
input returns from it. Page turns retain their source animation lock. Attribute
and Done actions have their separate source admission rules.

`NativeOwnedNifMenuSurface` renders original source geometry, controller channels,
geometry-local dynamic textures and triangle pointer targets. It preserves
authored point-light colors, dimmers and positions. The rendered-menu owner sets
their radius from the transformed model bound. It adds the declared fallback
light only when no source point light exists. BabyBook02 retains its authored
light. This surface also supports several declared model roots without adding a
cabinet to the book. It does not provide a Godot widget substitute for the book.

`FalloutSpecialAllocationSession` requires an explicit permanent-value getter
and absolute integer base-value writer for SPECIAL actor-value slots 5 through
11. The native integer getter floors the permanent float before editing and
budgeting. Increase, while the total is below budget, writes
`min(permanentInteger + 1, 10)` to the **base** value. Decrease writes
`max(permanentInteger - 1, 1)` to the base. Neither operation subtracts permanent
modifiers to force the resulting permanent value to match that target. Both
immediately refresh the live owner. Done validates the already-written total;
it does not commit a local draft. Retirement/cancellation retains those writes.
The declared default total is 35; the winning TTW book passes 40 explicitly.

The modal wrapper requires explicit player modal-input callbacks and preserves
the previous modal input, mouse mode and scene pause. Acceptance, cancellation,
tree retirement and failure cancellation release that lease once. An unbound
owner remains a visible error while the modal pause is retained. No source
Enable/DisablePlayerControls mask is replaced by this wrapper.

The actual winning `CG01SpecialBookSCRIPT` first guards the CG01 stage and stage
50 completion, then executes `SetStage CG01 50` **before** `ssbmp 40`. Stage 50's
authored result owns the quest timer; MenuMode holds it. Menu acceptance must not
set another stage or roll back the executed prefix. An unsupported menu command
after stage 50 retains that prefix, and another activation can fail the source
guard rather than reopen the menu. A source-faithful continuation therefore
requires a state from before activation or a reviewed command continuation owner.

The focused synthetic probe checks source declaration drift, complete paired
tables, default/explicit totals, fractional and negative permanent integer
reads, immediate base writes, modifier preservation, reopening and unsupported
owners. The native audit uses an isolated script host and explicit actor-value
callbacks; it cannot establish ordinary campaign progress. It checks the actual
activation order, source model input, all eighteen page transitions, initial
cover, back-cover limit, live digit changes, reverse pixel restoration,
cancelled edits, source Done, paused clocks and modal lease retirement. It reads
owned inputs without modifying them. Unsupported source activation retains its
stage prefix and guard. Constructor/live getter, base-writer and acceptance
callback failures retain readable errors and restore each prior input state on
retirement. Its one private PNG is for selected visual
inspection; the caller deletes it in `finally`, including failed runs. Recording
remains off.

Production command dispatch, the player permanent/base actor-value pools and
their save integration are deliberately unbound until their gameplay owners are
reviewed. Current player skill values must not be substituted for permanent
SPECIAL values. The existing detached Vigor draft is not silently changed by
this slice. Source fade/composition, renderer-fitted near/far clipping, bounds
through animated poses, keyboard shortcut/repeat timing, audio alignment,
language remapping, matched retail pixels and physical XR remain unverified.
No campaign, retail parity or headset acceptance claim follows from the fixture.
