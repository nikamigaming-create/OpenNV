# Flat gameplay completion work

The September 27 user direction prioritizes a functioning ordinary flat game:
NPCs and creatures respond, quest state progresses, looting and crafting work,
and saves preserve those outcomes. Detailed VR presentation work is not on this
immediate critical path. Both input modes continue to share gameplay state.
All ten requested mod targets and the complete NV/FO3/TTW scope remain open.

## Work order

1. **Actor combat and quest events.** Fix source admission, natural attacks,
   detection, movement, weapon reselection, hit/death dispatch, essential-actor
   recovery and XP. Exercise retaliation, hostile acquisition and neutral actors
   through the same owners. Autonomous NPC/creature conflict, assistance,
   peaceful relationships and resumed routines require no human input.
   A shootable actor is not a working encounter.
2. **The ordinary interaction loop.** Finish quantity-aware loot, equipment,
   recipe/perk requirements and transactions, barter, healing, repair and sleep.
   Keep item variants, counts, conditions and ownership through cold saves.
3. **Quest and population progression.** Use the winning quest/INFO/reference
   scripts and packages. Fix the first reached missing operation, replay the
   affected interaction and continue. Cover the opening, all tutorial branches,
   connected Goodsprings spaces, Primm and subsequent campaign/DLC branches.
   No injected quest stage or named-actor exception closes a failure.
4. **Shared mod capabilities.** Complete JAM/MCM, then TTW, then the remaining
   [ten targets](mod-compatibility.md). Resolve missing commands, conditions,
   events, UI, effects and save ownership across their full dependency stacks.
   A launcher checkbox or parsed plugin is not working mod support.
5. **Remaining campaign/system behavior.** Advance the source-derived route and
   [recovery requirements](recovery-checklist.md), including factions/crime,
   companions, leveling/perks, VATS, travel, persistence, all weapon families,
   radio/audio, UI and streaming. Missing systems stay explicit work items.

Work each failure through implementation and integration before moving on.
Use focused checks while editing and the existing repository checks when
publishing. Do not build another evidence framework or spend implementation
time making showcase footage. Recording stays off except for a chosen visual
defect. Finish each publication on synchronized main and begin the next block
on a new feature branch.

## Existing tools and how to use them

- `OpenNV.DevelopmentLab corpus` inventories the complete selected winning
  plugin graph and groups source admission failures. Its report does not say
  that parsed scripts execute correctly.
- The runtime reference, quest, combat and presentation observations identify
  the actual failed command, record/reference and owner. `cell-review` groups
  resident failures. Keep the first failure and its save/build identity.
- The existing reactive bot drives ordinary input for movement and supported
  interactions. Its campaign decisions and combat tactics are incomplete;
  extend those policies as required to replay a real gameplay chain.
- Compare retail only when intended behavior is uncertain. Match plugin stack,
  reference identity, saved state and input, then compare authoritative state
  and event order. Audio, UI, timing and pixels remain separate comparisons.
  Retail observations never supply OpenNV gameplay authority.

For each behavior, record **missing**, **implemented**, **failing in play** or
**working in its exercised scope**, together with the next owner. Do not convert
record counts, passed component checks or one completed route into a percentage
of the whole game. Unknown/mod-specific native extension behavior is unresolved
until its semantics have an independent implementation.

## September 27 actor corrections

- Compiled variable admission no longer parses unrelated executable lines.
  Matching duplicate declarations, digit-leading names and trailing author
  notes retain their compiled slots. Execution errors remain script failures.
- Detection and assistance use the same source-body sight query as combat,
  including self-exclusion and visibility of the target's own first contact.
- Acquisition and assistance include resident NPCs and creatures, independently
  of player presence. Current faction and aggression state determine eligibility;
  telemetry records candidate decisions. Nearest visible target selection is an
  explicit implementation policy; retail threat weighting remains unmatched.
- Weaponless NPCs use source unarmed skill/settings and humanoid attack clips.
  Absent optional weapon animation channels use the existing binding policy.
- Squat source BBX envelopes use a cylinder with their authored height and
  horizontal radius; taller envelopes keep the existing capsule. Movement,
  stepping and navigation sweep that actual native shape. This is an OpenNV
  controller policy, not measured retail collision parity.
- New deaths retain a pending `OnDeath` event and its elapsed gameplay time.
  The native event adapter waits for `fDyingTimer` and active speech, dispatches
  the killer-filtered source block and consumes it once. New v19 saves retain
  pending events; v18 and earlier supported saves remain readable. Existing
  corpses do not acquire invented historical events.

The death delay/filter follows the [GECK event contract](https://geckwiki.com/index.php/OnDeath).
The bounded unarmed base uses the documented
[actor-value formula](https://geckwiki.com/index.php/Weapon_Damage_Formula)
with winning settings. Complete perk/effect modifiers, hit-event batching,
essential recovery, XP, weapon exhaustion/reselection and combat tactics remain
open. Native component runs exercise detection, wall refusal, unarmed NPC and
creature retaliation, autonomous NPC/creature conflict and allied assistance
without player input, delayed quest updates and cold event state. Complete
ordinary encounters and campaign progression remain required work.

## September 27 interaction corrections

Container rows now transfer one item below the owned quantity threshold and
open `quantity_menu.xml` for larger stacks. Cancel preserves state, confirmation
rechecks availability, and shared inventory retains partial counts through
restore. Native checks and a rendered dialog cover this bounded behavior;
ammo/currency policy, theft and complete ordinary looting remain open.

Crafting rejects absent as well as insufficient ingredients before any output.
Duplicate requirements are aggregated and failed output resolution restores the
entire transaction. Recipe note/perk/equipment queries accept the source's
explicit player targets; map and interior-cell conditions use their existing
world owners. All 127 selected source note/perk conditions respond to changing
ownership in a native component check. GetDeadCount and complete recipe/quest
chains remain open. Reference-script GetDistance uses live three-dimensional
game-unit positions, and GetCurrentTime uses shared GameHour; these operations
do not themselves complete scripts that reach another missing command.

## September 27 resident schedules

Package selection evaluates PSDT time/calendar windows before CTDA conditions,
in authored priority order. It handles overnight starts, weekday groups and the
owned fixed calendar. NPCs and creatures receive the shared saved clock and
reevaluate at hour changes. The ten-second condition poll is an explicit OpenNV
policy; retail evaluation cadence and full package flags remain unmatched.
Schedule layouts follow the [PACK format documentation](https://tes5edit.github.io/fopdoc/FalloutNV/Records/PACK.html);
the [GECK package contract](https://geckwiki.com/index.php?title=AI_Package)
defines whole-hour schedule blocks and schedule-before-condition selection.

Playerless native NPC and creature motion checks use source root animation and
the actual collision owner. Non-player spatial queries and creature follow
speed use the actual actor/target state. These changes remove player dependencies
but do not implement sandbox choice, sleep/eat/guard procedures, package event
scripts/topics, off-residency simulation, complete saved package lifecycle or
retail behavior weighting. Those are required for the requested living world.

The ordinary flat copied-save run now loads the Primm exterior, observes hostile
acquisition and gunfire against an idle player, ED-E's response, and two active
patrol routes. Initial collision publication and temporary follower-target
absence no longer cause permanent startup AI failures. A prior strict-parser
change had also prevented Continue by rejecting four stored quest-script
owners. They now retain their saved state; ambiguous trailing Else syntax still
fails if reached. Cold restoration preserves all 446 original script owners.
The existing corpus audit reports 57 remaining source-body parse failures;
these counts do not certify execution or campaign support.

## Nonfatal combat response

Qualifying NPC and creature hits now evaluate the source hit-reaction IDLE tree,
interrupt the active attack/reload and publish its KF before combat resumes.
An ordinary flat Primm run exercised the arm response through mouse firing and
saved the active animation phase. NPC and gecko component checks also exercise
fresh-skin resumption, repeated hits and subsequent attacks without a player.
Healthy limbs and radial explosions do not invent anatomical stagger reactions.
Forced reactions, blend timing, hit scripts and broader combat tactics remain
open; see [the implemented contract and limits](combat-hit-reactions.md).
