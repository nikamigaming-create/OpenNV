# Current work

## Active batch: FNV autonomous campaign playthrough - Goodsprings progression

Work continues on user instruction ("FNV 100% go"). Active branch is
`codex/fnv-goodsprings-progression` based on `main`. The full required runtime gate
(`Test-GodotRuntime.ps1` and `git diff --check`) passes cleanly with 0 warnings,
0 errors, clean git diff, and verified Godot runtime audits.

### Verified capabilities and progression

1. **Active quest goal selection**:
   - Quest filtering in `RuntimeCoordinator.Bot.Campaign.cs`: admitted active quests alongside running quests (`!quest.Completed && (quest.Running == true || quest.Active) && quest.Objectives?.Any(objective => objective.Displayed && !objective.Completed) == true`), correctly prioritizing the player's active quest `VCG02` ("Back in the Saddle", Priority 1075) over distant quests across the wasteland (`VMQ01:30` at Primm).
   - Cross-cell portal navigation: set portal approach mode to `"travel"` when portal entrance doors are not resident in the current exterior cell (`mode = world.IsResident(action) ? "interact" : "travel"`), preventing premature interaction attempts on non-resident doors.

2. **Wasteland navigation bound**:
   - Navigation execution bound in `ReactiveReferenceBot.cs`: extended execution timeout to 900 seconds while actively navigating towards distant goals (`_mode == "travel" || _phase != "awaiting-interaction"`), allowing the bot to complete long exterior wasteland traverses without stalling or timing out.

3. **Goodsprings exterior traversal**:
   - Autonomous bot selects `FalloutNV.esm:10a214:3` ("Back in the Saddle", Objective 3: Talk to Sunny Smiles) and navigates down the Goodsprings street towards the Prospector Saloon door (`FalloutNV.esm:10636f`), steadily closing distance across multi-segment navmesh corridors.

### Next steps

1. Execute required gate (`Test-GodotRuntime.ps1` and `git diff --check`).
2. Commit and push `codex/fnv-goodsprings-progression`, open PR, verify CI, and merge into `main`.
3. Launch autonomous campaign bot on next branch to complete entry into Prospector Saloon, initiate Sunny Smiles dialogue, and progress the tutorial.
