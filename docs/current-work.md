# Current work

## Required outcome

Reach Primm by ordinary movement, activate and repair broken ED-E, recruit him,
leave Nash Residence together, and exercise following, combat and loot in flat
and Elliott Tate's OpenXR Simulator. Deliver a labelled single-eye showcase and
an asset-free experimental build. The user's flat/headset playtests are required
before any golden release. This outcome is not yet achieved. No subagents.

## Verified candidate

PR #30 is merged at `e9f8851776e5a0dcee647a8c5d4a82b5a5a221ef`.
The experimental Windows build is published as
[`v0.1.0-experimental.20260920`](https://github.com/nikamigaming-create/OpenNV/releases/tag/v0.1.0-experimental.20260920).
The next branch is `codex/scripted-message-results`. Private evidence
is under `local/status-audit-20260920/`; no retail files, saves or captures belong
in Git or the release.

Ordinary flat and simulator dialogue/barter takes exist. The private first-look
side-by-side and the illustrated playtest document are under
`local/recordings/playtest-20260920/`. Actual flat Pip-Boy Stats/Items illustrations,
dialogue facing and a reloading-bench 9mm breakdown were viewed. They do not
establish complete inventory/crafting or physical-headset acceptance.

Jolt fixes the reproduced source barrel/rubble contact lock. The actual save
escaped and continued south. Native gecko death/sever/contact fixtures pass.
Persistent leveled-template selections now feed appearance, inventory, scripts,
voice, factions and combat. A source appearance audit admitted 6,861/7,681
references; this is not eligible-spawn or final-pixel coverage. The Primm-area
run has 24 creature presentations. Competing NPC armor now selects compatible
slots and retains spare armor as loot; exact retail tie ordering is unverified.

Exterior presentation retains a bounded inactive fringe of LAND and reference
nodes. A native boundary-reversal fixture confirms same-node/material reuse,
disabled inactive collision/process and bounded eviction. Source LAND/static
shaders and LOD use complementary coverage through a moving transition band.
Shader loading passes; the moving transition still requires final visual review.
The live route still has measured individual uploads around 77-90 ms and grid
commits around 50-60 ms. Hitching is not resolved. Clouds and water-tower cutouts
were viewed; broader alpha, vegetation and prop-wind behavior remain open.

## Ordinary route and current blocker

The flat bot completed the Primm approach in 132.85 seconds, observed arrival at
Nash's real door, activated it and entered Nash Residence. The genuine ordinary
entry save is `tmp/development-lab/release-flat-20260920-q/save.json`. Its ED-E
state is dead with no persisted physical pose. The private 90-second
`local/recordings/playtest-20260920/flat-primm-route.mp4` records the exterior
walk. This is one completed flat route, not XR route or campaign acceptance.

The bot now uses A* over source NAVM portals, tests resident portal clearance
against the actual capsule/floor, and refines only the next eight metres with
native swept queries. Failed local corridors are excluded temporarily while A*
searches alternatives. Segment completion triggers a replan from observed player
position. Source-polyline distance replaces straight-line lookahead. Synthetic
checks pass alternate-route selection, rejection when every portal is blocked,
low-headroom traversal, wall avoidance, separate floors and unloaded refusal.
Only ordinary flat/simulator input may move the player. The q process was stopped
for export; inspect the newest session before issuing input.

## ED-E and remaining owners

Ordinary activation opened damaged ED-E's authored repair menu. His presentation
was wrong: flying idle added about 1.9 metres while ignored XRGD specifies a
near-zero local offset. XRGD decoding now preserves ordered source bone transforms
and duplicate Havok part numbers. Native initial-pose and cold-restore checks pass
for all ten ED-E bodies. Actual flat and both simulator eyes now show ED-E on
the counter; both modes opened his real repair menu through bot input. XRGB biped rotation
remains an explicit unsupported branch; source binding does not prove its runtime
pose. Bot corpse aiming now uses live physical centers instead of rest-mesh bounds.
The next acceptance is ordinary repair with legitimate skills/components,
recruitment, then source following and door transfer. Current flat/XR sessions
are `release-flat-20260920-message-a` / `release-xr-20260920-message-a`; recording is off.
The source's two OnActivate blocks previously queued a superseded, unanswerable
prompt. Presentation now follows the current result slot and drops obsolete
callbacks/pending saves. The actual exported flat and simulator runs both reached
Repair -> Parts -> Leave on the first selections. Skills/components remained
unchanged and the ineligible repair actions stayed absent. The private
21-second `OpenNV-ED-E-flat-vr-repair-menus.mp4` shows this sequence and labels
recruitment pending. This is menu navigation, not completed repair or retail
message-order parity.

Zero-health source actors initialize as corpses. The owned repair-message test
passes. Non-player MoveTo retains source identity, disabled
state, destination residency and cold state; its owned fixture moves the real
working ED-E from its source parent to Nash's cell. This is a component fixture,
not an ordinary repair. Restraint and player-teammate flags persist.

Live repair/recruitment remains blocked by missing gameplay owners: source
recruitment perk/faction/combat-style effects, creature follow packages and door
transfer, scaled actor health and creature weapon combat. Repair must respect
actual skills/components. Do not replace these with named success paths. Actor
encounter-zone policy, some abilities/assets and broader scripts still fail
closed; all-people/all-creature/combat coverage cannot be claimed.

## Verification and publication

`runtime-gate-primm-pose.log` contains `OPENNV_CSHARP_GODOT_GATE_PASS` and
`owned-primm-pose.log` passes the selected owned-data audit. `global-route-native.log`,
`global-route-bot.log`, `authored-ragdoll-contract.log` and
`authored-ragdoll-native.log` pass their focused changes. Packaging/notices are
verified. The 102.9 MB asset-free experimental ZIP passed packed-resource and
archive boundary scans. Its packaged executable passed cold Continue and
ordinary ED-E inspection in flat and the simulator. SHA-256:
`08c55d05bc4c34d574a7f92e5b8cbc940b9667a603ab8f919d486c14fc83ca29`.
The release is not a golden build; recruitment, follower behavior and Aid use
remain unfinished. Keep recording off except selected checks;
remove temporary raw frames after review/export.

The message fix passes `message-runtime-gate.log`, `message-contract.log` and
`message-owned.log`, plus ordinary flat/XR input and reviewed footage. The public
experimental package above predates this fix. Aid still has no Use action:
next bind owned ALCH/EFIT/MGEF behavior, inventory consumption and saved effects
to the existing vitals and flat/wrist UI before recording healing.

See [status](status.md) and [implementation plan](implementation-plan.md) for the
preserved full scope and evidence levels.
