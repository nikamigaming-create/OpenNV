# Current work

## Active owner: Goodsprings speech, furniture and idle continuation

The coordinator plans/reviews; user-authorized GPT-6.1 Sol High implementers share
one checkout and feature branch. The central executor alone owns builds, game
input and Git. Follow [implementation-plan.md](implementation-plan.md).

Acceptance requires retail systems/presentation through Goodsprings to the Strip,
sustained native 3840x2160 at 60 FPS indoors/outdoors, and separate physical XR
acceptance including 144 Hz. Historical save compatibility is expendable;
current-build saving remains a gameplay feature. Recording and trace are off.

Merged PR #327 at `8dd4053544f75e139533a9e0ee317524112bba94` passed its local
mandatory gate, selected owned NPC checkpoint audit and all five hosted checks.
Local main equals origin/main. Current branch `codex/goodsprings-continuation`
starts from that baseline. Eighteen unrelated
generated classic import EOL changes remain preserved and excluded.

## Current verified result

Current ExportRelease MVID `bb459162-42f6-440b-bd8f-d297aaa9560b` has a clean
Debug solution build and release export. Selected retained speech source/clock,
reference identity and furniture placement checks pass. Its full mandatory gate
and diff check pass; checked PR publication remains pending.

Ordinary Continue restored tutorial 50 from the canonical profile at
`local/integrated-baseline-20261028/gameplay`. Native 3840x2160 play then killed
the next hostile geckos, completed Sunny's three-response dialogue and marked
quest 10a214 completed (stored stage 50, objectives through 60 complete). Gathering
quest 15d912 entered stage 10 with objective 10 displayed and incomplete. Both
ingredients still need gathering in the current checkpoint. Player HP remained
100. Cooking and a complete Caravan match remain untested. Another actor's
legitimate target kill was accepted through source quest progress; the runner's
missing player-attribution receipt was not counted as a player kill.

F5 command157 generation 1 completed at 07:36:11.819 UTC with save and cleanup
errors null, cell 0e19c5, HP 100 and no blocked capture owners. The save retains
NPC 1629a8 occupied on floor-sitting marker 17 and NPC 1629a9 approaching the
lawn-chair marker 14. Floor activity reselection/re-entry no longer produced its
former procedure failure; physical lawn-chair entry remains unverified. Detailed
current-build state is private. The game closed through normal Quit/Yes and
confirmed source-owner disposal; no interactive game is running.

This successful forward save is now canonical, SHA256
`4A610F8232E585A124A177FF4CEC421887148B765E3C5B4914032B07C13D88CB`.
Its next normal Continue has not yet run. Do not replay the opening or overwrite
healthy progress while capture owners fail.

## Current patch and next owner

- Speech source resolution retains the saved LVLN 101c8f choice 101c8d and its
  VTYP 00002d. An authored response lacking both audio and LIP now has an explicit
  source-text-byte-count timer through live fAIDefaultSpeechMult, then ordinary
  next-tick completion/results/exchange ownership. INFO 17af3d has three source
  bytes and the installed multiplier 0.15, producing 0.45000002 seconds. The
  selected owned-data and synthetic checks pass; this INFO was not encountered
  again in the integrated run, so its live completion remains unverified.
  Ambiguous media, explicit SOUN and audio-absent LIP timing remain strict.
- Furniture approach retains the NAVM corridor, then reaches the authored entry
  root through the native capsule route. It does not replace the root with its
  projected endpoint or weaken support checks. The compact placement regression
  passes; both reached actors physically occupied floor sitting in actual play.
- Occupied Sandbox furniture reports its native chair/sleep procedure. Actual
  play exposed the next owner: normal activity reselection/GetUp must report
  Sandbox phase 1 (public procedure 21), distinguished from parent-package exit
  by the retained exit reason. That extension passes the affected contracts and
  actual forward saving without the former blocker. Other unqualified entry or
  exit phases still refuse a guessed procedure.

Publish one checked PR. Next normal Continue
should resume gathering quest 15d912: Broc Flower 15d981 and Xander Root 15d982,
then Sunny and campfire 0cdb95. Do not follow unrelated quest 0842dd as crafting
evidence. No broad feature expansion or optimization in this batch.

## Remaining boundaries

Active non-radio speech saving still lacks serialized channel continuation;
waiting for a line to finish is not that capability. Background creature Find/
Food 1651b1 remains unbound; its qualified native sequence is Travel -> Acquire
-> Wander -> Done, not Sandbox Eat. Candidate selection/rights are still open.
Repair/Mod, broader magazine use, cooking and a complete Caravan match need
ordinary coverage. VATS has earlier selection/cancel/two-miss evidence; hit/damage,
cinematic and matched retail behavior remain unverified.

Native 4K60 is unmet. The rolling 512 travel draw intervals measured median
21.2512 ms, p95 31.2714 ms and p99 38.2948 ms. Retained owner attribution over
16,692 draws identifies CreatureSandbox about 104 s (nested SandboxMovement
100 s), PackagePursuit about 65 s and capsule sweeps about 60 s. Do not add nested
totals. These shared movement costs are the next measured performance owners
after saving works; no optimization has been implemented in this batch.

Full retail pixels, FaceGen, audio, crime/factions, merchant/crafting coverage,
other creature procedures and physical XR hands/render/session acceptance remain
open. No campaign, parity or performance completion is claimed. Keep detailed
design in [runtime-repair-design.md](runtime-repair-design.md).
