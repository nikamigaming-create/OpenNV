# Current work

Updated 2026-10-21. User priority is **fully playable flat Fallout: New Vegas**.
Root works alone and owns code, builds, input and Git. The active branch is
`codex/fnv-physical-prop-persistence`: repair the confirmed moved-object reset
on cold Continue. Sandbox attachment continuation is repaired in PR #250;
autonomous activity selection and execution remain unimplemented.
The user explicitly treats development as greenfield: preserving old development
saves is not a priority. Favor working ordinary gameplay over compatibility work
or expanding diagnostic frameworks. Retail inputs remain read-only.

## Current owner

The change binds each resident non-actor dynamic NIF body to its reference
save owner, using model hash and body block identity. Schema v55 retains world
transform, linear/angular velocity, sleep and wind randomness; v54 and prior
admitted saves keep their original state without invented historical motion.
The owned skull test passes JSON roundtrip, detached cell reconstruction,
continued gravity/motion, sleeping restore, source-drift rejection and retention
on unload. Detached preparation cannot replace the live physical save callback.
Ordinary Continue, walking and aiming reached the skull; Z lifted it from its
shelf and released it onto the floor. F5 committed v55. A separate process used
normal Continue and restored the exact saved position; a visual check showed the
skull on the floor, and another F5 completed. Temporary screenshots were removed
and both sessions exited. Synthetic checks, the final owned check, the complete
local runtime gate and diff check pass. Review and publication are pending. Saving
while actively holding a body currently refuses rather than dropping its grab
continuation silently. Full held-input continuation and retail physics parity
remain open. After publication, continue ordinary tutorial progression; Sandbox
selection remains a population failure, not a prerequisite for that route.

Repair the ordinary New Game route: Doc Mitchell's player/head positioning and
questionnaire, physical object handling including the tutorial skull, Sunny's
shooting and creature encounters, gathering and campfire cooking, then campaign
progression through Primm/ED-E and the Strip. These are gameplay requirements;
existing component passes do not close the user's reported failures.

PR #243 is merged with local and hosted runtime checks passing. Flat Grab uses
the actual owned NIF dynamic body, collision and winning weight limits. Normal
Continue from v52 acquired the skull with Z, lifted it from the shelf and dropped
it onto the floor, delivering two source grab/release cycles. F5 saved in v53.
A fresh Continue restored the player and consumed event marks, but reset the
skull to its original shelf. The active branch repairs this confirmed defect as
described above; publication remains pending.

PR #244 is merged with local and hosted runtime checks passing. Normal New Game
and cold Continue complete Vigor, the seated questionnaire, tag skills and traits,
exit the couch with E and save with F5. Dialogue follows the actual animated
speaker and furniture entry clears the activation pitch. Normal standing dialogue
also works: Doc's equipment handoff and Hardcore prompt reach stage 200, and the
real exit door loads Goodsprings and saves character creation complete. Dialogue
zoom, turn timing and matched retail framing remain open.

The shared font lifetime repair removes a loading tip's disposal of its borrowed
source-session atlas. A fresh ordinary Continue and exterior/interior/exterior
door cycle now retain HP/AP, reticle, activation text and message drawing, with
no disposed-font faults. F5 completes outside. Debug build, selected owned
gameplay checks and the full runtime gate pass.

PR #246 merged the checked HUD target recovery. Ordinary play reproduced the
linked-door error, then recovered Open on the saloon front door, entered with E,
recovered Sunny's Talk prompt and played her genuine greeting. Selecting her
training topic changed the source quest and sent Sunny through the actual exit
portal. This starts the tutorial; shooting, creatures and cooking remain open.
Linked-door lock lookup is repaired by merged PR #249 as described below.

PR #247 merged actor initialization, dialogue wait and cell retirement. Sunny's
source editor wait now uses native approach and activation-distance waiting.
Ordinary play opens the saloon, saves while she waits, cold-loads that wait and
walks into her voiced automatic greeting without pressing Talk. Normal Goodbye
then F5 commits again, with no pending procedure or corpse capture blockers.
Moving dialogue retains its native package pose and clock. Source-dead NPCs skip
living AI selection and EVP. Synthetic ownership and cold-state contracts pass;
the required local and hosted runtime gates pass.

The user also reports missing sweeping/farming idles, dialogue and broken Victor.
Live Goodsprings settlers select Sandbox PACK procedure 12, which the NPC runtime
rejects before any work behavior starts. Creature Sandbox and additional Patrol
inputs also remain unimplemented. The save failures reached through the saloon were traced to a
source corpse incorrectly selecting a living patrol, and detached preparation
diagnostics poisoning a different native owner's corpse capture status. Only the
bound owner can now change that status. Capture precedes cell teardown, deferred
corpse construction completes before retirement, and snapshots defer while real
corpse ownership is incomplete. Opaque or genuinely unready state still refuses
capture. Ordinary F5 and cold continuation now pass through the affected route.
PR #248 merged creature Patrol execution. Victor's source selection is
VGSVictorCircuitPatrol, previously refused by the absent creature procedure.
Its eleven-point source route includes waits and a final reference arrival result
that resets a quest variable. The new path uses native movement, source heading,
arrival results and retained patrol state. The reached Securitron wheel uses a
Bethesda transform subclass with the published inherited layout; the reader now
admits it and owned key sampling passes. Ordinary exterior Continue moves Victor
with his source walk clip and commits F5. A centimetre-scale NAVM/collision floor
difference then exposed a too-small arrival tolerance; it now matches the existing
native NPC patrol tolerance while requiring physical floor contact. Cold
Continue restores the saved motion without repeating POBA, and the first six
points run. The cemetery descent then repeatedly targets a blocked source guide
midpoint. Actor route prefixes now retain the authored next triangle as their
arrival region instead of requiring that exact midpoint; an initial long slice
retains its progress plane. Native capsule, floor and residency checks remain
mandatory. Synthetic actual movement crosses an occupied midpoint in both
directions, refuses a full wall and retains wrong-floor/source-boundary refusals.
Continue from the saved failure now crosses the descent and the remaining source
markers. Victor reaches home, its XPPA result changes bVictorPatrol from 1 to 0,
and FNV retains the final wait through automatic polling before POEA and POCA.
Explicit EVP remains eligible. Source facing publishes its actual saved rotation.
F5 commits the completed route; a separate process restores the exact saved home
position and variable without restarting the retired patrol, then commits F5.
Native owned facing/idle checks pass. Package route queries avoid combat's
component-region search: selected repeat-run source-query samples are 1.1-1.5 ms,
with native query slices below 1 ms; this is not a whole-frame performance claim.
The local and hosted full gates pass. Nearby visual and matched retail
timing/framing remain unverified; no complete Victor/population parity is claimed.

PR #249 repaired the missing Open action on a source-linked door. The access
owner formerly refused a lock declared on the opposite side. The implementation
now resolves the local lock first, then the linked side without creating a remote
instance during queries. Mutations retain the actual owner. Synthetic source/key,
dynamic, local-priority and cold-order contracts pass, as does the owned saloon
source-pair check. Ordinary Continue and walking reach the actual door with visible
E Open / Door / Locked. E reaches the source lock refusal and F5 commits. A separate
process continues that save, reacquires the door by ordinary aiming, displays the
same Open/Locked text, respects the lock and commits F5 again. The full local gate
passes, and hosted checks pass. The PR is merged and local main was verified
equal to origin/main before starting this block. The source door is genuinely
locked; lockpicking itself remains unimplemented, and no locked traversal or
retail parity is claimed.

Next population repair: source Sandbox execution for sweeping/farming and ambient work.
Goodsprings settlers reach procedure 12 with an editor-position radius and a
source schedule, but no native Sandbox owner exists. Private static research of
the matching owned retail build identifies the settings-to-duration formula and
per-category tables. Its input state, target scoring, probability transitions,
occupancy and activity continuation are still being traced. No new Sandbox
gameplay or parity claim is established. Implement the shared source procedure,
authored idle/furniture use and save continuation; verify through ordinary play.
The owned source graph places DefaultRakingMarker 448.99 game units from one
settler's editor position. It selects LooseRaking and its actual aoRaking ANIO
model. The source animation has loop and sound keys; the attached rake has no
internal animation controllers. Static source ANIO attachments now retain their
winning record/model hashes alongside the selected idle clock. Native owned
raking checks reconstruct the actual rake, compare every actor bone and object
transform after cold restoration, then compare the continuing phase and sound
history. Changed models and independent controllers remain refused. Synthetic
source drift, attachment-set and v53 admission checks pass; v54 owns the new
bindings. The native fixture deliberately retains the actual unsupported Sandbox
package error: it verifies attachment persistence, not ordinary autonomous work.
Ordinary Continue from a copied v53 save loads the existing exterior, and F5
commits v54 through the normal save owner. The original slot is unchanged.
The required local runtime gate and diff check pass.
No native Sandbox work is fixed yet. Selection, movement, occupancy, interruption
and activity continuation remain unfinished population work.
The broken-SMG script has an extra source and compiled `endif`; the interpreter
rejects it. Held-object save continuation and tutorial/campaign progression
remain open.
Recording is off during development. Use specific temporary captures only when
needed to inspect a visual defect, and remove them after inspection.

## Remaining work

Fix the shared source-driven owners reached during ordinary play. Creature
attacks must acquire, approach, animate, hit or miss, damage and recover correctly.
Tutorials must perform their real inventory, quest and world effects, including
crafting and physical interactions, with save and cold continuation. Continue
through the full requested route and expand to remaining campaign behavior;
the entire flat game remains incomplete until it actually works.

Preserved FNV defects are in [status.md](status.md) and
[implementation-plan.md](implementation-plan.md). Rendering, population, combat,
scripts, collision, audio and persistence still have unresolved failures. No
campaign or 1:1 retail parity completion is claimed.

## Preserved work

FO1/FO2 work is preserved. PR #242 (`codex/classic-script-audio`) remains separate
from this FNV branch; its hosted checks pass. Classic script audio details and
remaining campaign owners are in [classic-audio.md](classic-audio.md),
[classic-fallout-plan.md](classic-fallout-plan.md),
[classic-script-events.md](classic-script-events.md) and
[classic-interactions.md](classic-interactions.md). No classic full playthrough
qualifies. Package g remains rejected for physical VR use.
