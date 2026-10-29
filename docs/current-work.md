# Current work

## Active batch and ownership

One GPT-6.1 Sol High executor owns implementation, builds, game input and Git.
The coordinator reviews the final coherent diff before publication. No parallel
writers or per-decision approval loop remain. Follow implementation-plan.md:
shared systems, headless execution, integration, then representative retail
comparison; gameplay is not the primary missing-feature discovery loop.

Checked baseline PR #333 is c6ac937697d713cbd8b20c3014e4c35693f4163f.
Its required local/owned gates and all five hosted checks passed; main was
reconciled with origin/main. Active branch: codex/script-state-owners. Eighteen
unrelated classic import EOL changes remain preserved and excluded.
No game is active. Recording and render trace are off.

## Implemented candidate

World-owned mutable FormLists retain ordered additions over immutable winning
source members. AddFormToFormList, ListGetCount and ListGetNthForm share both
parsed-script hosts. Any existing member is a qualified no-op. Cold restoration
validates source identity and the complete prefix before replacing live state.
Intrinsic PlayerRef identity is supported even without a placed source record;
arbitrary dynamic references and mutation of serialized null-member layouts remain
refused. Read-only consumers preserve first-match/null termination and validate
only visited members; malformed tails are not evaluated after a native short circuit.
Inventory counts/equipment/actions, hit matching, ammunition/weapon presentation
and NPC/player armor expansion read the same owner. Appearance invalidation and
initial synchronization observe already-mutated lists.

A single actor-value command owner supplies Set/Mod/Force/Damage/Restore routing,
argument conversion and pool changes to quest/reference/player hosts. Health
and limb restoration retain their specific existing owners. Set/Mod/Force use
signed-integer extraction; Damage/Restore preserve Float32 fractions. Negative
amounts needing unqualified value-direction flags remain explicit. Behavioral
AI-data pools use the selected source template and cached immutable provenance;
mutable modifiers and effective aggression/confidence remain separate. Pools and
list additions persist in current-build saves. This does not implement every AV
formula, effect or skill operation.

Focused production-handler contracts pass: list ordering/no-op/empty/intrinsic
player identity, cold/source-drift rejection, live consumers, template ownership,
behavior effects, both script hosts, fractional operations and health restore.
The selected owned audit passes: the actual GRA startup prefix changes 27 lists
with 195 additions, then stops explicitly at SetEssential (zero completed quests).
Six actual player AI values and intrinsic PlayerRef cold restoration also pass.
Lonesome Road reaches unowned AddItemToLeveledList before its FormList prefix and
is not counted as list execution proof. The final required gate and diff check pass.
No unsupported command is skipped or given a default.

## Actual player result and retained screenshots

Immutable exported DLL MVID b2a220ad-522b-4c9e-8cc9-a7c523e74f03, SHA256
A5AA722971D4037B39407257BB2CDC4A3FA095265665493EA60E588888A9BF8F,
ran an ordinary fresh New Game. Recorded Doc speech, name acceptance, Reflectron
sex/race confirmation and manual house traversal worked. E activated the source
Vigor Tester; ordinary buttons allocated [6,6,6,5,5,6,6] and accepted all points.
VCG01 reached stage80. Doc physically walked to and occupied the psychology chair.
Opening/speech errors were null; couch/psychology follow-up was not activated.
No new engine blocker was established in this bounded run.

F5 command450 completed at 2026-10-29T18:57:10Z. Fresh checkpoint:
local/integrated-baseline-20261028/gameplay/script-state-fresh-checkpoint.json,
2,429,252 bytes, SHA256
73A1248C07B3FD0FF4B4832C5350D0D8075F8993DCDE20B8A3270BF086E9E930.
The previous 02ABC cooking/traversal checkpoint remains untouched; its absent
historical idle field must not be backfilled or replayed as a known refusal.
Continue the fresh stage80 save for the next needed packaged-launch acceptance.
PID26272 closed normally; source-owner disposal and process absence were verified.

Six requested full-resolution screenshots and capture metadata are retained in
local/screenshots-20261029-script-state. They show awakening, recorded dialogue,
Reflectron, world/HUD, Vigor interaction and seated Doc. Actual client resolution
was 3840x2124, constrained by the window work area; this is not a native 4K or
matched-retail parity claim. Recording stayed off throughout.

## Delivery priority and next systems

Owned/full validation and diff check pass. Complete checked publication of this batch.
Then prepare an accurately labeled asset-free experimental Windows playtest using
Build-OpenNVPlaytest.ps1 and Package-OpenNV.ps1. Inspect loose files, actual PCK
entries and final archive; exclude owned/derived retail content, saves, captures
and private diagnostics. Verify packaged launch, real quest/script interaction,
combat and useful saving using current fresh progress. Publish only after those
checks and required gates pass. Full parity, native 4K60 and physical XR144 are
not prerequisites for this limited release and remain unclaimed.

The user also requests a continuous proof video from that exact packaged build:
show identity/launcher, Continue fresh stage80, finish Doc and exit to Goodsprings,
then ordinary Sunny dialogue/shooting/combat/loot/crafting and save/quit/Continue.
This is planned acceptance, not a completed route. Record real elapsed time and
game audio if available; retain the original requested take and relevant blocked
segment. Report actual resolution/performance without treating encoded FPS as
game FPS. Recording stays off outside this specific run; exclude unrelated apps
and clean temporary frames. No staged scene, forced quest change or intro replay.

The next actor-state batch must join mutable actor-base flags, essential damage/
knockdown/recovery/query/save behavior; adding SetEssential registration alone
would leave the existing essential-hit refusal intact. Preserve the BaseID versus
per-reference extension distinction. Compiled SCDA decoding/execution remains a
broad missing scripting system; SCTX parsing and extent accounting are not a VM.
Use the shared world host. Runtime reference allocation/identity/lifetime/save
is another explicit world gap: current resolution owns source forms and PlayerRef,
and PlaceAtMe owns EXPL only. Do not fabricate records or private dynamic members.

Remaining condition/function/context families, actor procedures, source-food
provenance, raven locomotion, active-speech saving, Sleep bed entry/wake and
lawn-chair entry remain open. Source counters and plausible screenshots do not
establish campaign, parity, physical XR or performance completion.
