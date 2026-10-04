# Actor death history and result conditions

GetDeadCount reads cumulative deaths for the specified winning NPC_/CREA base
from shared reference lifetimes. Weapon damage and Kill use the same once-only
death transition. Counting follows successful death-item preparation; a failed
transition changes neither health nor history. Repeating Kill on a corpse adds
nothing. Disabled, unloaded and deleted references retain their history, and a
later actor lifetime cannot lower it. A templated child keeps its own base
identity rather than adding a death to its appearance/script donor.

The [GECK contract](https://geckwiki.com/index.php/GetDeadCount) distinguishes
cumulative history from current corpse state and requires an actor base argument.
Scripts, fallback quests, native NPC/creature packages, unloaded package
evaluation, dialogue and recipe/result conditions use the same C# owner. Source
corpses have no consumed runtime killing transition. Player death, resurrection
and respawn behavior remain separate runtime boundaries; the later-lifetime test
is a controlled shared-state fixture, not an implemented resurrection command.

Schema v37 retains positive per-reference counts independently of injury state.
Cold validation rejects negative history, non-actor owners and killed actors
with zero history. v36 remains readable: its once-only granted death inventory
proves one consumed transition, while source corpses prove none. That supported
legacy runtime owned no resurrection or respawn command. Reading does not edit
the original save; older schemas cannot store the new history field.

Quest result entries admit CTDA run-on scope only when their host explicitly
owns it. The campaign host binds the existing player inventory and other source
condition queries. Unsupported subjects, flags and later results stay visible;
retry cannot replay a consumed entry prefix.

Full-reader checks cover repeated/failed deaths, child template identity, cold
and unloaded state, a later lifetime, typed shared/fallback script calls and
invalid restoration. Stage checks cover explicit player inventory, host
admission and retained failure prefixes. The owned audit selects the unchanged
Escape stage2 entries and evaluates Amata's original death predicate before and
after an isolated source actor death and cold restoration. It neither runs the
wake-up commands nor establishes ordinary campaign progress or retail parity.
