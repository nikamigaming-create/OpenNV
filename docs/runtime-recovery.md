# Runtime recovery

The current objective is a workable flat/OpenXR playtest candidate. See
[current work](current-work.md) for the active candidate and
[status](status.md) for verified capabilities and remaining gaps. Historical
migration code is not the current runtime authority.

## Current owners

| Behavior | Shared runtime owner | Remaining acceptance |
| --- | --- | --- |
| Quest and INFO execution | `FalloutQuestStages`, `FalloutReferenceScripts`, `FalloutConversation` | Full source command coverage and broader quests |
| Ordinary dialogue | `RuntimeNativeConversation`, `FalloutDialogueConditions` | Further actors/topics and physical controller playtesting |
| Player health/limbs | `GameplayVitals`, `FalloutPlayerVitals`, winning BPTD thresholds | Environmental radiation, healing effects and complete modifier pools |
| Trade and crafting | Source merchant/recipe resolution plus shared inventory transactions | Ordinary stations, more merchants and cold continuation |
| Actor combat | `RuntimeNativeActorCombat`, `FalloutReferenceWorld` | Full tactics, damage rules, events/XP and general corpse stability |
| Doors and streaming | Source XTEL links, retained reference world and exterior residency | Full connected-world traversal and streaming performance |
| Persistence | `FalloutNativeCampaignSave` and shared C# snapshots | Active dialogue/movie/menu continuation remains unsupported |
| OpenXR | `NativeXrRig` plus shared menu/gameplay owners | Simulator functional replay, then the user's physical headset test |

## Scope after the playtest candidate

The complete opening, Sunny tutorial, broader Goodsprings/Novac/Strip travel,
all weapon families and supported campaign/plugin behavior remain objectives.
A component check or short video does not complete that scope.

Use ordinary input, preserve source identity and state, save, restart and
Continue. Fix blocking interactions before expanding the route. Visual polish
and streaming cleanup follow the workable candidate. Keep source assets and
private captures out of public packages. A golden release requires the user's
flat and physical-headset playtests; matched retail parity requires its own
independent evidence.
