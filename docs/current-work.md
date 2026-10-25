# Current work

## Active batch: FNV autonomous campaign playthrough - Goodsprings progression

Work continues on user instruction ("FNV 100% go"). Active branch is
`codex/fnv-autonomous-playthrough` based on `main`. The full required runtime gate
(`Test-GodotRuntime.ps1` and `git diff --check`) passes cleanly with 0 warnings,
0 errors, clean git diff, and verified Godot runtime audits.

### Verified capabilities and progression

1. **Autonomous chargen completion**:
   - Trait menu: tagged unselected selectable trait rows with `chargen` metadata and matched continue button `LUM_ContinueButton` alongside `TM_DoneButton`.
   - Couch exit: added furniture active/phase observation to `ReactiveReferenceBot` and automated stand-up activation pulse when starting movement while seated.

2. **Trigger contact and farewell dialogue**:
   - Added close destination standoff (`Distance = 0.5f`) for `travel` mode goals in `ReactiveCampaignBot`, enabling the player to step firmly inside trigger volume `FalloutNV.esm:107236` (`GSDocMitchellExitTrigger`) and advance `VCG01` to Stage 115.
   - Verified Doc Mitchell farewell greeting, item/Pip-Boy handover, and Hardcore message dialog choice.

3. **Exterior transition and save admission**:
   - Fixed save checkpoint admission: removed overly restrictive `PendingPackageEventCount == 0` check which was blocked by unloaded actors in non-resident cells (e.g. `0d7f59`).
   - Added `checkpointReady` support for finite, non-looping `PlaySound`/`PlaySound3D` script voices (`!voice.Request.Source.IsLooping`) in `FalloutScriptSounds.cs`, allowing one-shot audio stingers (such as `ui_radio_signalfound2.wav` on cell entry) to finish without blocking save creation.
   - Guarded manual save preparation in `RuntimeNativeManualSavePreparation.cs` to check admission before preparing finite save drains.
   - Verified checkpoint creation in Goodsprings exterior cell `FalloutNV.esm:0da726` and cold continuation from checkpoint.

4. **Wasteland navigation**:
   - Autonomous bot navigated over 60 meters along the Goodsprings street towards the Prospector Saloon door (`FalloutNV.esm:10636f`) for quest `VMQ01`.

### Next steps

1. Commit and push `codex/fnv-autonomous-playthrough`, open PR, verify CI, and merge into `main`.
2. Resume autonomous campaign bot on fresh branch to continue from the Prospector Saloon into the Sunny Smiles tutorial and Goodsprings quest chain.
