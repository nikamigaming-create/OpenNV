# Current work

## Active owner: integrated gameplay recovery

The coordinator plans and reviews. GPT-6.1 Sol at High reasoning is the sole
implementation executor. Follow [implementation-plan.md](implementation-plan.md).
Use one shared checkout, one active feature branch and one integrated build.

User acceptance: native 4K60 indoors and outdoors, every retail system and
presentation through Goodsprings to the Strip, including combat, crafting,
looting, quests, factions/trading, Caravan and VATS. Keep going; arrival alone
does not satisfy this. No complete-game or nearly-finished claim is warranted.

Latest user direction: greenfield. Old saves, historical restoration and
backward compatibility are not requirements. Replace broken implementations as
needed. Advance fresh ordinary gameplay, with current-build saves as a player
feature rather than a reason to preserve old state machinery.

Current branch: `codex/integrated-gameplay-continuation`, based on freshly fetched
`origin/main` at `b94d8e8df22fc70fc9d3280bf8c2d7581b212eb1`. Existing changes are
preserved and unpublished. Their initial file hashes and the prior detailed work
notes are retained privately under `local/integrated-baseline-20261028`.

Release and Debug build; formatting/analyzers and managed/native gate stages
pass across resumed runs after repairing the dialogue-probe dependency and stale
skin-lifetime fixture. A single fresh full gate remains required before pushing.
The selected owned creature publication check passes. Product ExportRelease
uses the normal launcher script. Recording is off; builds and gameplay are
serialized.

Two reviewed runtime repairs are implemented in this block: stopped creature
alert changes now keep the retained activity revisions consistent (focused
regression passes), and live-root creature placement now invokes the shared
source-publication owner after configuration. The second fixes a missing join
that could leave streamed pending-Sandbox creatures frozen and unsaveable.
Ordinary gameplay validation of these fixes is still pending.

## Stopping point and resume

Stopped at the user's explicit request on 2026-10-28. No new implementation,
full gate, push, PR or merge is authorized during this wind-down. Relevant
integrated source/tests/docs are checkpointed locally on the existing branch;
publication remains pending a fresh full gate and review.

The final ExportRelease product MVID is
`9d38a82d-9efe-42fd-949a-1b86ba6d46a7`. Ordinary New Game passed the formerly
failing post-load movie skip. Ordinary Load of the current opening save now
restores the name menu after loading releases its temporary pause; accepting it
resumes source progression without a detached-node error. Opening presentation
and source ticking activate only after the state owners and live tree are ready.

Final native 3840x2160 Male-page and initial Strength 5 / 05-point frames were
compared with private retail references. Shared menu materials now keep source
textures/colors and authored lights without inheriting cell fog; the existing
blurred-world compositor is visible. Reflectron uses its source vertical slope;
Vigor's decoded wide/narrow depth branches now select the source-correct depth.
The confirmed flat-color and framing defects are removed. Fine FaceGen shading,
exact pixel/color parity and other presentation channels remain open. Selected
private diagnostics stay under the ignored gameplay directory, outside commits.

Ordinary SPECIAL allocation then completed, advancing to VCG01 stage 80. F5
saved successfully (v65, Courier, Strength 10 and other SPECIAL values 5, HP 100).
The canonical current save and profile are under
`local/integrated-baseline-20261028/gameplay`; this is the new playthrough, not a
historical restore. PID 19632 exited normally and disposed its source owners.
Recording, render trace and campaign automation are off. No game/build is running.

On resume, first run the selected owned check and one fresh full required gate;
review the integrated diff before checked publication. Current focused menu,
movie-pause and owned LoveTester checks pass, but resumed gate stages from
before the final fixes are not a fresh integrated pass. Continue ordinary play
from the current save after the integration boundary. The next reached gameplay
repair is NPC Sandbox Furniture: candidate enumeration currently refuses chairs
before selection. Reuse source reservation/approach/entry/exit while retaining
Sandbox package lifecycle and events. Do not suppress the failure.

## Material open work

- In-progress creature staging and animation-index changes have selected checks
  but no integrated gameplay result. Preserve clock/random state, selection,
  cancellation and source ownership when publishing staged actors.
- Creature Find food execution/Allow Killing, Travel Allow Falls and full
  current-location package lifecycle remain incomplete. Offline source readers
  and transactions are not a functioning actor procedure.
- Inventory Repair/Mod/Drop/Hotkey actions are hidden; VATS lacks an input-driven
  runtime owner; a qualifying Caravan command refuses match launch despite
  existing offline rules. Implement these reached retail actions in the shared
  gameplay owners. Crime/detection consequences and owned lock/hack admission
  are also incomplete. Crafting, loot and barter have owners but fresh-route
  coverage remains open; merchant restock/pricing inputs remain incomplete.
- Source failures include SpeedTree, distant LOD materials, model constraints
  and other actor/rendering inputs. Missing entities remain visible divergence.
- Native 4K60 and physical XR 144 Hz targets remain unmet/unverified. The latest
  recorded grid commit was 193.77 ms with 129.47 ms creature publication. Build
  overlap contaminated later performance intervals; no speedup is established.
- Physical XR acceptance failed previously, including malformed rifle
  hands/forearms and render/session issues. Flat and XR must share game/save
  state; no headset-ready or retail-parity claim is justified.

Run the selected owned-data check and the mandatory runtime gate before a checked
PR merge. Update this file with the next verified state and owner; keep detailed
technical context in [runtime-repair-design.md](runtime-repair-design.md), not
an expanding task history here.
