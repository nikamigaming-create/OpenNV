# Current work

## Active owner: Sandbox activity integration

The coordinator plans and reviews. User-authorized GPT-6.1 Sol High implementers
share one checkout and feature branch; the actor executor alone owns builds,
game input and Git. Follow [implementation-plan.md](implementation-plan.md).

Acceptance requires retail systems and presentation through Goodsprings to the
Strip, sustained native 3840x2160 at 60 FPS indoors and outdoors, and separate
physical XR acceptance including the 144 Hz target. Arrival or selected checks
do not establish completion. Historical save compatibility is expendable;
current-build saving remains a gameplay feature. Recording and trace are off.

Branch `codex/sandbox-activity-execution` is based on merged PR #325,
`a8c6b15485a5040cf61a7eecb08ded45da361547`. Local main equals origin/main.
Eighteen unrelated generated classic import EOL changes remain preserved and
excluded. PR #325 passed its required local and hosted checks. This unpublished
batch passed its fresh mandatory gate and selected owned native NPC furniture/
checkpoint audit. Both builds had zero warnings/errors; formatting, contracts,
launcher and native Godot checks passed. Hosted checks are pending publication.

## Verified gameplay and next owner

ExportRelease candidate `06d9e517-0d91-4e53-b0ee-c3fa50bb1c7f` compiled with zero
Debug warnings/errors and continued the canonical VCG01 stage-200 save. Both
exterior NPCs 104f03 and 104f0a executed source LooseRaking through End, retired
the request and returned to marker selection or Wander without AI/animation
errors. Ordinary F5 command 103, generation 1, completed; its checkpoint retains
two marker continuations and request graphs. Cold restoration of those newly
saved graphs remains unverified. The interrupted held-phase guard is now fixed
in source, after this candidate's gameplay check, and passes the final gate.

Ordinary continuation reached Sunny in the saloon, cell 106185. F5 generation 2
completed before the next conversation. Sunny INFO 10a1e4 / quest 10a214 stage 10
then failed while Cheyenne executed script idle 0c1991: the creature script-idle
owner explicitly refuses finite completion/return. This is the next runtime
owner, not a bot policy failure. Combat was not reached. The game closed normally
and released its source owners. Shutdown also logged a Godot
`ExceptionUtils.LogException` NullReferenceException; process exit and owner
disposal are verified, while clean shutdown logging remains unresolved.
The canonical current-profile save under
`local/integrated-baseline-20261028/gameplay` now resumes meaningful stage-200
saloon progress; do not replay character creation or the questionnaire.

The actor batch shares furniture physical execution with Sandbox without
completing the parent package, retains sampled daily scheduling, and selects
before checking executor support. Doc is no longer a permanent save blocker:
his real bed marker/reservation, source approach and unchanged parent package
state can save. Bed entry/loop/exit and ordinary chair entry remain unverified.
Marker execution now retains current/queued/outgoing requests, source clocks,
ease-out retirement, ANIO transforms and source cursor. Snapshot blending uses
the shared signed-priority kernel; explicit skeleton resets preserve owned NIF
scalar scale without overwriting subsequent live animation or VR transforms.

Ordinary Hotkey assignment and physical key 1 equipped/drew the Weathered 10mm.
Earlier integrated gameplay passed Stimpak Drop/Take, 9 -> 8 -> 9, through the
settled source NIF body and E activation. Repair/Mod, per-instance magazines and
Take All conservation compile; focused probe cases passed the central gate
and broader ordinary coverage remains open. Caravan and VATS runtime, input,
menus and consequences compile, but a complete ordinary match and VATS engagement
remain untested. No retail parity claim follows from these integrated owners.

Next publish this reviewed integration through one checked PR after the fresh
local gate and selected owned audit passes. After merge, fix the reached
creature finite idle completion/return on a fresh feature branch, then continue
Sunny's tutorial and actual combat from the current save. No further feature
expansion before this publication boundary.

## Material open work

- Full retail behavior/pixels, FaceGen shading, animation and audio parity remain
  open. Sandman/Cannibal special-action execution remains unsupported.
- Native 4K60 travel/streaming/combat is unmet; observed stream commits exceeded
  budget. No controlled performance or speedup claim is established.
- Crime/detection and owned lock/hack consequences, complete merchant behavior,
  crafting/loot/barter coverage, full Caravan and VATS remain open.
- Other creature packages, LOD materials, SpeedTree, constraints and explicit
  source divergences remain. Bed/chair end-to-end gameplay is unverified.
- Physical XR still has malformed rifle hands/forearms and render/session
  failures. Flat and XR share authoritative gameplay and saves.

Keep detailed design in [runtime-repair-design.md](runtime-repair-design.md).
