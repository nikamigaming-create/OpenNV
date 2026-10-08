# Native NPC travel and door access

Actor navigation now uses the shared capsule refinement spacing after a failed
coarse search. Actor radius already includes source/instance scale. Refinement
keeps the original capsule, source NAVM intent, floor rules, 512-node limit and
shared frame budget. It changes the search lattice rather than physical clearance.
The existing native narrow-passage tests cover forward/backward traversal and
blocked walls, wrong floors and unloaded geometry.

An NPC may use an ordinary locked door whose retained ownership names its base
or exact placed actor. The reference event owner first requires a living resident
NPC, an active accessible door, supported activation-parent rules and source
motion. The final interaction rechecks the shared ownership permission. Access
does not remove the lock for another caller. Faction/rank/global ownership, NPC
keys, creatures and actor portal transfer retain explicit unsupported boundaries.
Source scripts and runtime ownership overrides keep their existing authority.

Synthetic access checks cover base/exact ownership, foreign callers, conditional
ownership refusal, retained ownership changes and cold restoration without
unlocking or opening the door. Independent owned-source checks retain the
declared actor/door identities and locks in FNV, FO3, TTW and the combined stack;
these managed queries do not certify their scene or campaign behavior.

In the actual cold-loaded standalone FO3 opening, Dad's 0.33 m capsule previously
exhausted the 0.33 m refined grid beside the open playpen. The shared 0.165 m grid
finds the physical route. His original close-gate package reaches its marker and
advances the original quest to stage 16. The room door declares that NPC's base
as owner and remains locked; the native NPC activates it once, waits for its
source animation, walks through, and the original completion advances stages
18 and 20. Ordinary player movement then leaves the playpen and reaches stage 30.
No actor placement, stage write or collision exception substitutes for arrival.

The stage-14 checkpoint remains immutable. Manual saves of the later state
currently fail on active background radio speech, whose persistent voice/result
continuation is unbound. The later state is live evidence rather than a cold
checkpoint. The SPECIAL book, continuing opening, reverse NPC door traversal,
Megaton, matched retail motion and physical-headset acceptance remain open.
Recording is off; no temporary frames or owned data are included in publication.
