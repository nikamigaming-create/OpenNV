# Current work

## Active objective

Complete the bot-driven TTW campaign from the genuine Fallout 3 checkpoint through
all Vault 101 quests including the G.O.A.T., Megaton, authored Union Station
power/ticket/train travel and the Mojave, then Benny and continued campaign play.
The October 2 user request resumes implementation and ordinary bot input.
The bot may read the complete owned graph and authoritative observations, then
acts through ordinary walk/look/activate/dialogue/menu controls. Stage injection,
teleportation and fixture outcomes never count as campaign progress.

Complete all nine JAM modules, MCM, TTW dependencies and compatible guide
recommendations, Benny's selected deleveling/confiscation/recovery and the ten
original mod targets. Preserve general Nexus script/dependency compatibility,
source-authored classic screens, shared flat/OpenXR gameplay and saves, audio,
HUD, smoothness and truthful journey video. All 36
[recovery requirements](recovery-checklist.md) remain open. Follow the
[implementation plan](implementation-plan.md), [mod compatibility](mod-compatibility.md),
[flat work order](flat-gameplay-plan.md) and [JAM/MCM plan](jam-luna-max-plan.md).

## Verified starting point

Ordinary opening input reaches the toddler playroom. The prior run reaches
CG01 stages 0/5/10/12/14/16/18/20/30/40. Its genuine pre-book autosave is v26;
book activation previously entered stage50 before failing at `ssbmp 40`.
The saved pre-activation book has no script error. The manual stage16 slot remains
intact. The initial route was human-driven; bot campaign progress starts here.
The repaired bot cold-loads that genuine save, walks/aims/activates the real book,
and observes source stage50 and menu1060. Ordinary source-geometry pointer input
allocates40 points, retaining immediate shared BASE writes, then Done restores
gameplay. Saving while the book is active correctly refuses continuation loss.
The source timer and Dad's return/speech advance70/72/73/74/75/80. Dad's shared
Escort procedure acquires the player and begins native movement. The closed,
unlocked main door blocks his route; ordinary bot activation opens it, Dad
continues and waits for the trailing player, then the bot follows him to the
source destination. His completion advances90/100 and stops/completes CG01.
CG02 enters0/5, then the calling CG01 script retains an unbound `player.AgeRace`
failure before birthday-room relocation. This run's gameplay is bot-driven from
the copied stage40 checkpoint; no stage injection or teleport was used.
The complete birthday quest, Vault exit, Megaton, train and Mojave remain unreached.

The genuine stage40 and stage16 saves are backed up separately under
`local/ttw-bot-resume-20261002/`; their original run remains unchanged.

## Active implementation

The candidate integrates reference access, player SPECIAL pools and save
binding, production book/scoped quest observations, source Escort motion and
quest-update commands. Bot navigation now distinguishes partial/projected
endpoints, retains bounded closing progress and replans for a moving target.
Activation results require effects or matching requests from the actual source
OnActivate blocks; unrelated timers, stages and menu changes cannot complete it.

The next gameplay owner is the shared signed race-age transition reached by CG02,
followed by its inventory reset and equipment commands. Automatic NPC door
activation also remains an active general navigation gap.
The settled post-book stage50 slot and stage80 door-interaction autosave retain
the player's allocated BASE pools; the latter also retains acquired Escort
progress. A fresh process cold-loads stage80, restores acquired Escort without
replaying its start event, and follows through eight moving-target replans to
the same source stage100/CG02-entry failure without a bot error. The stage80
backup is now a verified reusable checkpoint for this bounded continuation.
NPC automatic route-door activation remains unbound; the successful run used
ordinary bot door input. No collision geometry was changed to pass the route.
Retain pause/modal/mouse restoration and refuse active-continuation saving.
Never clear saved faults or replay a consumed source prefix.

The combined `scripts/Test-GodotRuntime.ps1` gate and selected installed-stack
reference-access, player-value, quest-update and JDC audits pass. Final regressions
retain queued autosaves during Escort initialization and preserve failed package
results on unscripted actors across cold restoration without replay. Publish
each completed block through a checked PR, merge and synchronize main.
JAM's typed INI decoder, selected getter and loaded-plugin query pass focused
synthetic and owned checks. The selected JDC initializer completes 360 audit
invocations without error; its recurring callback next faults at its equipment
query. Shared objective completion and deferred quest-HUD cancellation also
pass focused contracts, the owned CG01 stage90 source audit and ordinary source
continuation through stage90. Native HUD timing and pixels remain unverified.
Equipment/extra-reference and JBTMCM object-script clock owners remain incomplete.
The live checkpoint stack has18 plugins and six TTW dependency roots; JAM and
Benny are not mounted. Their module acceptance remains separate work.

## Private continuation

Original run: `tmp/development-lab/ttw-departure-clock-20261001/`.
Active bot run: `tmp/development-lab/ttw-bot-20261002/`; exited normally after the
retained CG02-entry AgeRace failure. Source stages, bot input, build hash and
selected observations are private. Genuine stage40, stage50 and stage80 backups
remain available; do not load the failed continuation as a repaired prefix.
Private preserved saves: `local/ttw-bot-resume-20261002/`.
Never publish saves, retail-derived media, binary observations or extracted assets.
Preserve protected user saves under `local/playtest-20260927-world/` and
`local/playtest-20260920-companion/`.

Frame recording is off during development, headless checks and ordinary bot
investigation. Enable only for a selected visual check; delete temporary frames
in cleanup/finally paths. Existing requested selected media stays in
`local/recordings/ttw-current-opening-20261001/`. The complete journey reel remains
pending. Proper gurney departure, cold actor faults, timing, smoothness and
matched retail/OpenXR presentation remain unverified.
