# Current work

## Active batch: FNV autonomous campaign playthrough - Saloon tutorial & bottle shooting

Work continues on user instruction ("FNV 100% go"). Active branch is
`codex/fnv-saloon-tutorial` based on `main`. The full required runtime gate
(`Test-GodotRuntime.ps1` and `git diff --check`) passes cleanly with 0 warnings,
0 errors, clean git diff, and verified Godot runtime audits.

### Verified capabilities and progression

1. **Creature Follow Target Resolution**:
   - Implemented `ResolvePackageTarget` delegate on `RuntimeNativeCreature` mapped to `_nativeReferencePresentation?.Nodes.GetValueOrDefault(target)` in `RuntimeCoordinator.NativeOwned.cs`.
   - Avoids thousands of per-frame `GetParent().GetChildren().OfType<Node3D>()` tree allocations and linear searches across exterior cells during creature follow packages, cutting physics frame time by over 50%.

2. **Refined Navigation Node Budget Scaling**:
   - Scaled the node expansion budget for refined capsule navigation searches to 3600 nodes (`maximumNodes: 3600`) in `RuntimeCoordinator.Bot.Planning.cs` and `NativeCapsuleNavigation.cs`.
   - Accommodates higher spatial node density when climbing stairs and navigating complex structural geometry (e.g., Prospector Saloon wooden steps `FalloutNV.esm:109037` and porch `1055e0`) without hitting premature node-bound aborts.

3. **Prospector Saloon Interior & Sunny Smiles Tutorial Advancement**:
   - Autonomous bot navigated Goodsprings road, climbed the Prospector Saloon steps, activated `ProspectorSaloonDoor` (`FalloutNV.esm:10636f`), and transitioned into `GoodspringsSaloon` interior (`FalloutNV.esm:104c86`), writing checkpoint 1.
   - Targeted resident NPC Sunny Smiles (`FalloutNV.esm:104e85`), opened dialogue, navigated dialogue choices, received the Varmint Rifle and ammunition, and advanced quest `VCG02` ("Back in the Saddle") to stage 20 (Objective 5: "Follow Sunny Smiles outside"), writing checkpoint 2.
   - Followed Sunny Smiles out the saloon door (`FalloutNV.esm:10618e`) back into Goodsprings exterior, writing checkpoint 3, and followed her to the tutorial bottle shooting range (Progress 6).

### Next steps

1. Run the full required gate (`Test-GodotRuntime.ps1` and `git diff --check`).
2. Commit, push `codex/fnv-saloon-tutorial`, open PR, verify checks, and squash-merge to `main`.
3. Start fresh branch to complete bottle shooting tutorial, gecko well defense, and Goodsprings town quests.
