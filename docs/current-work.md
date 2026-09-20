# Current work

## Required outcome

Repair and recruit ED-E through ordinary gameplay, leave Nash Residence together,
and fight/loot alongside him in flat and Elliott Tate's OpenXR Simulator. Deliver
an honest left-eye side-by-side and an asset-free experimental build. The user's
flat and physical-headset playtests are required before a golden release. This
outcome is not achieved. Work in this task without subagents.

## Candidate and evidence

Main is PR #32, `615228a163f62eb78bd7423a884b16ab34728b79`.
The active branch is `codex/creature-follow-packages`. It adds source creature
Follow/direct Dialogue movement, persistent package clocks/poses and talk
history, and scaled actor health from retained level selection. ED-E's source
330-unit follow fixture moves 7.3 metres, stops at 4.7 metres and preserves its
position/clock through cold restoration. Source package priority and the
500-unit alternative are checked. This is a native physics fixture on a
synthetic floor, not ordinary recruitment or paired gameplay acceptance.
See [creature packages](creature-packages.md) and [actor damage](actor-damage.md).

PR #32 implements shared Stimpak use and timed health effects. Actual flat and
simulator wrist input each consumed one Stimpak (9 -> 8), with both final eyes
inspected. These checks began at full health; post-combat healing is unverified.
The new save schema is v15; v14 Aid saves remain readable.

The published [experimental build](https://github.com/nikamigaming-create/OpenNV/releases/tag/v0.1.0-experimental.20260920)
predates the message, Aid and package changes. Its asset-free ZIP SHA-256 is
`08c55d05bc4c34d574a7f92e5b8cbc940b9667a603ab8f919d486c14fc83ca29`.
Do not describe it as an updated or golden release.

## Ordinary playthrough

The genuine checkpoint `tmp/development-lab/release-flat-20260920-q/save.json`
reached Primm by ordinary movement, activated Nash's door and entered. It has
Repair 31 and no full repair-component set. Broken ED-E now rests on the counter
in his authored XRGD pose, with folded antennas and a reachable repair menu.
Both modes have actual Repair -> Parts -> Leave footage. Repair/recruitment has
not been completed. Never grant skills/components or mark a fixture as gameplay.

The saved simulator session `release-xr-20260920-aid-a` uses PR #32 code in Nash
and retains eight Stimpaks; its process was stopped. The current flat session is
`release-flat-20260920-supplies-a`, cold-continued from q using the PR #32 export
to collect legitimate repair supplies. Inspect its fresh `input/live-state.json`
before input. Frame recording is off. The older flat message session saved
outside Primm after a blocked gecko approach; it did not establish combat.

The bot uses source NAVM A* and bounded native capsule refinement. It completed
the flat Primm route but still failed approach to counter scrap and an exterior
gecko. A target reference's position is not necessarily a reachable standing
position. Fix general approach/line-of-sight planning as needed.

## Next owners

Complete the checked publication of the current package slice, then use a fresh
feature branch. The immediate gameplay blockers are recruitment perk/faction/
combat-style effects, ordinary repair eligibility, follower door transfer and
creature weapon/non-player combat targeting. Primm NPCs also reject competing
weapons; implement general selection/unarmed fallback. Follow only has component
proof. Reached unsupported packages/effects remain explicit errors.

Keep the focus on ED-E outside and participating in a fight in both modes.
After that, capture damage -> Stimpak -> loot -> save -> cold Continue and finish
the functional reel. Existing dialogue/barter/crafting/Pip-Boy takes and the
illustrated private playtest document are under
`local/recordings/playtest-20260920/`; the latest 21-second repair-menu video is
`OpenNV-ED-E-flat-vr-repair-menus.mp4`. It is labelled recruitment pending.

## Gates and remaining scope

Private logs are in `local/status-audit-20260920/`. The Aid gate and owned
Stimpak audit pass. The current slice passes `companion-runtime-gate-publish.log`,
`companion-package-owned-publish.log` and `creature-follow-native-publish.log`.
The relocation check preserves rejection of the unbound original holding-cell
zone and admits the actor after source MoveTo into Nash. No retail data, saves,
captures or private executable
analysis belongs in Git or release archives. Keep recording off during builds
and checks; remove temporary raw frames after inspection/export.

The exterior still has measured 77-90 ms uploads and 50-60 ms grid commits.
Moving LOD transitions need final visual review. Clouds/water-tower cutouts were
viewed; vegetation/alpha and all eligible actors remain unverified. The source
appearance audit's 6,861/7,681 admissions do not establish spawn completeness.
Encounter zones, broader scripts/effects and physical headset acceptance remain
open. Preserve [implementation plan](implementation-plan.md), [status](status.md)
and the full [recovery checklist](recovery-checklist.md).
