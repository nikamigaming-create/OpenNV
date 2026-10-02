# Current work

## Active objective

The immediate objective is birth to Megaton, the authored train connection and
the Mojave through ordinary input and persistent state. Complete TTW and all
nine JAM modules with MCM. The campaign includes Fallout 3's opening, all
Vault 101 quests including the G.O.A.T., Megaton, authored Union Station
power/ticket/train travel, the Mojave, Benny and continued play. Retain TTW
dependencies, compatible guide recommendations and the original mod targets.
All 36 [recovery requirements](recovery-checklist.md) remain open. Follow the
[implementation plan](implementation-plan.md), [mod compatibility](mod-compatibility.md),
[flat work order](flat-gameplay-plan.md) and [JAM/MCM plan](jam-luna-max-plan.md).

## Verified campaign state

The ordinary bot route from the genuine human-reached toddler stage40 checkpoint
activates the source SPECIAL book, allocates40 points through its actual pointer
controls and reaches CG01 stage80 through source timers and speech.

A fresh Continue from the genuine closed-door stage80 restores acquired Escort
progress. Close following obstructs Dad among the living-room furniture. Moving
the player toward the source main door and activating it through ordinary input
lets the integrated finer capsule route continue. Following Dad then reaches
CG01 stages90/100, completes and stops CG01, and enters CG02 stages0/5. No
collision, actor position or quest stage was edited. Automatic escort-door
recovery without player activation still needs acceptance.

The birthday race/inventory prefix executes. Amata's child FaceGen assembles,
receives source head tracking and plays her owned introduction voice and lip
morphs. The security actor now assembles and receives source head tracking.
The actual source player relocation enters the birthday room. Speech still
fails for Beatrice, who has no visible resident runtime actor for SayTo. The
glasses environment texture also leaves a birthday actor missing.
Birthday completion, Vault exit, Megaton, train and Mojave remain unreached.

## Current implementation block

[Model alternate textures](model-texture-indices.md) follow the source geometry's
3D index in scene-child traversal order. Stored labels do not validate or
redirect the index; later duplicate indices replace the texture. The immutable
order is reused within each decoded NIF. Synthetic checks cover nested/reordered
children, repeated/stale names, replacement order, unreferenced geometry,
particle index consumption and invalid-owner refusal. Both affected owned
security actors assemble with all selected parts, materials and inventory. The
ordinary retry passes security head tracking and enters the birthday room.

[Rigid FaceGen components](actor-face-attachment.md) retain the selected head
inverse bind and animated head without requiring biped Prn markers. All three
children pass complete owned assembly, and ordinary Amata speech passes.
Matched retail pixels and physical OpenXR acceptance remain unverified.

Shared [race aging](scripted-race-age.md), [inventory commands](inventory-script-commands.md)
and [scripted challenges](scripted-challenges.md) retain authoritative state and
cold values. Campaign schema v27 reads the genuine v26 Escort/SPECIAL checkpoints.
Never clear saved faults or replay a consumed source prefix to manufacture
continuation. The [skin-root repair](actor-skin-root.md) and
[route-door owner](npc-door-navigation.md) retain their component evidence.

## Next owners

1. Trace Beatrice's birthday SayTo absence against winning enable/state rules
   and native speech/process behavior. Missing actors and faults remain visible;
   do not invent an actor or clear the consumed birthday prefix.
2. Bind the glasses' authored 2D environment-map behavior without substituting
   a guessed cubemap. Retry the genuine checkpoint and continue birthday play.
3. Repair sampled short bot endpoint crossing. Ordinary follow can overshoot
   a two-centimetre arrival radius; closer follow goals currently continue.
4. Continue birthday interactions, G.O.A.T. and Vault escape to Megaton, then
   the authored train route. ForceRadioStationUpdate still needs a real station
   owner when reached. Complete all nine JAM/MCM modules and their dependencies
   under the same campaign and persistent gameplay owners.

The live checkpoint stack has18 plugins and six TTW dependency roots. JAM and
Benny are not mounted in that stack. Their acceptance remains required work;
initializer/parser admission is not full mod support. Flat and OpenXR share
authoritative gameplay and saves.

## Private continuation

Run `tmp/development-lab/ttw-bot-20261002/` exited through ordinary Quit after
the birthday speech failure. Recording remained off. Genuine stage16, stage40,
stage50 and open/closed stage80 backups are under
`local/ttw-bot-resume-20261002/`; closed stage80 is the next retry. The private
manifest and focused actor/campaign reports retain hashes and source evidence.
Do not resume the failed CG02 prefix as though its source effects completed.
The original `tmp/development-lab/ttw-departure-clock-20261001/` is unchanged.

Protected user saves under `local/playtest-20260927-world/` and
`local/playtest-20260920-companion/` remain untouched. Never publish saves,
retail-derived media, extracted assets or binary observations. Keep recording
off during development and remove temporary frames after selected visual checks.
The requested complete journey video remains pending.
