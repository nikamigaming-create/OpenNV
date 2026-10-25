# Current work

## Active batch: FNV Campaign autonomous progression (VCG02 and VMQ01)

Work continues on user instruction ("Consolidate all that shit down and be working on one", "get this thing 100% playable end-to-end, one-to-one with retail").
Active branch is `codex/fnv-campaign-bottle-shooting` based on `main`. The full required runtime gate
(`Test-GodotRuntime.ps1` and `git diff --check`) passes cleanly with 0 warnings,
0 errors, clean git diff, and verified Godot runtime audits (`OPENNV_CSHARP_GODOT_GATE_PASS`).

### Verified capabilities and progression

1. **FNV Tutorial Quest (`VCG02` - "Back in the Saddle") Fully Autonomous**:
   - **Stage 10 (Bottle Shooting)**: All 3 sunset sarsaparilla bottles (`10a202`, `10a203`, `10a204`) acquired, targeted with >0.998 aim alignment, fired upon, and verified hit.
   - **Stage 20 (Goodsprings Trail Follow)**: Followed Sunny Smiles south over 180m past Goodsprings Cemetery down to Goodsprings Source (Well 1).
   - **Stage 30 (Well 1 Gecko Combat)**: Engaged aggressive geckos (`10a1fe`, `10a1fd`) live in-engine via single-ray combat skill, verified damage and deaths, repositioned upon miss/obstruction, and talked to Sunny Smiles.
   - **Stage 40 (Well 2 Escort & Exterior Traversal)**: Traversed rocky exterior cell terrain down to Well 2, reached Sunny Smiles, initiated dialogue, selected responses, and advanced to Stage 50 (Progress 14).
   - **Adaptive Navigation Refinement**: Added corridor prefix-length halving and arrival tolerance in `RuntimeCoordinator.Bot.Planning.cs` to prevent 8-meter path cuts from stalling on static terrain obstacle margins.

2. **Main Quest Handoff (`VMQ01` - "They Went That-a-Way")**:
   - Automated curriculum seamlessly transitions between high-priority campaign objectives and main quest goals (Deputy Beagle / Primm).

3. **Full Runtime Gate Verification**:
   - Release and Debug C# builds, managed and native contracts, launcher tests, and Godot runtime audits pass cleanly (`OPENNV_CSHARP_GODOT_GATE_PASS`).
   - Clean git diff with 0 warnings, 0 errors.

### Next steps

1. Push `codex/fnv-campaign-bottle-shooting`, open checked PR, verify CI, and squash-merge into `main`.
2. Continue autonomous campaign bot progression along the Highway 15 corridor towards Primm and Novac.
