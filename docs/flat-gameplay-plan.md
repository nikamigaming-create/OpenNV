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
