# Current work

## Active objective

Complete TTW and all nine JAM modules with MCM through ordinary input and
persistent state. The campaign objective includes Fallout 3's opening, all
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

The birthday race/inventory prefix executes. Amata's child FaceGen now assembles,
receives source head tracking and plays her owned introduction voice and lip
morphs. The current continuation stops before birthday-room relocation because
the security outfit's alternate-texture mismatch leaves its actor missing.
The glasses environment texture also leaves a birthday actor missing.
Birthday completion, Vault exit, Megaton, train and Mojave remain unreached.

## Current implementation block

[Rigid FaceGen components](actor-face-attachment.md) use the selected head's
source inverse bind and the receiving animated head even when their export
omits biped Prn data. The synthetic native audit covers explicit/implicit
attachments, replaced export transforms, animation and rejection cleanup.
The complete mounted-stack actor audit assembles all three birthday children
with source materials and inventory. PP lighting flag0x40 is retained as source
state independently of the no-lighting property's falloff behavior. Ordinary
Amata speech now passes the previous missing-mouth boundary. Matched retail
pixels and physical OpenXR acceptance remain unverified.

Shared [race aging](scripted-race-age.md), [inventory commands](inventory-script-commands.md)
and [scripted challenges](scripted-challenges.md) retain authoritative state and
cold values. Campaign schema v27 reads the genuine v26 Escort/SPECIAL checkpoints.
Never clear saved faults or replay a consumed source prefix to manufacture
continuation. The [skin-root repair](actor-skin-root.md) and
[route-door owner](npc-door-navigation.md) retain their component evidence.

## Next owners

1. Bind the security outfit's alternate textures from source model behavior,
   including repeated shape names and record indices. The current missing
   actor is the first birthday Look/speech blocker.
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
the security-actor failure. Recording remained off. Genuine stage16, stage40,
stage50 and open/closed stage80 backups are under
`local/ttw-bot-resume-20261002/`; closed stage80 is the next retry. The private
manifest and focused FaceGen/campaign reports retain hashes and source evidence.
Do not resume the failed CG02 prefix as though its source effects completed.
The original `tmp/development-lab/ttw-departure-clock-20261001/` is unchanged.

Protected user saves under `local/playtest-20260927-world/` and
`local/playtest-20260920-companion/` remain untouched. Never publish saves,
retail-derived media, extracted assets or binary observations. Keep recording
off during development and remove temporary frames after selected visual checks.
The requested complete journey video remains pending.
