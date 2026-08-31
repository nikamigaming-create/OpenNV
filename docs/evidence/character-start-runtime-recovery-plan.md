# Character-start execution plan

This is the work order. Finish each item, verify it in the running game, and
move immediately to the next. The final movie is rebuilt only from the corrected
runtime.

## 1. Fix actor skin once for every game

1. List every actor visible in the FO1, FO2, FNV, and FO3 character-start clips.
2. Rebuild all of their actor caches from the owned game data with the current
   compiler. Do not reuse older sidecars that omitted skin metadata.
3. Make every source `SHADERSKIN` surface—head/neck/body/exposed arms/both
   hands—use the same FaceGen complexion target.
4. Keep every outfit surface out of the complexion transfer.
5. Make actor loading fail when a visible hand or exposed skin surface lacks its
   skin contract, so this cannot be sporadic again.
6. Capture each actor front, side, and back under neutral light and correct any
   remaining face/neck/wrist/arm seam before continuing.

Result: one shared fix covers Doc Mitchell, Doctor Li, Dad, Mom, players, and
the classic-game 3D analogs.

## 2. Fix the New Vegas opening for real

1. Read Doc Mitchell's effective package order, conditions, target references,
   chair reference, chair NIF marker, entry/loop/exit animations, root motion,
   dialogue cues, and quest-stage transitions from the owned data.
2. Put those resolved values in the generated opening contract. Do not put Doc
   offsets or guessed yaw values in runtime code.
3. Drive Doc through that contract:
   - start on the correct chair marker;
   - face the player/patient from the marker's authored orientation;
   - perform the correct seated work;
   - stay on that package while character creation is open;
   - exit the chair once with the correct animation root;
   - continue to the correct next target.
4. Remove the behavior that sends him backward, toward the wrong table/bed, or
   walking incorrectly at the player.
5. Run the whole opening and compare the chair, facing, creator pause, exit, and
   next movement against retail in order. Do not cut around a bad section.

Result: Doc's complete visible cue works correctly in normal gameplay and the
movie can safely spend more time on character creation.

## 3. Fix the Fallout 3 birth room

1. Rebuild Doctor Li, Dad, Mom, and all visible player variants with the shared
   skin fix.
2. Verify Doctor Li's exact owned outfit record and female mesh.
3. Fix the common outfit/skeleton shape-transform handling that is exploding or
   distorting her doctor shirt. Do not add a Doctor Li visual offset.
4. Resolve each participant's effective package, target, position, facing,
   animation, dialogue cue, and quest stage from the owned profile.
5. Make grounding preserve source X/Z and facing so it cannot move actors out of
   their package alignment.
6. Run the birth-room sequence through character creation and the first
   transition; correct every actor that is off cue or facing the wrong way.

Result: the room looks occupied by correctly dressed people performing the
right scene, rather than disconnected actors posed around the camera.

## 4. Finish Fallout 1

1. Apply the shared retail lighting setup to every premade 3D preview so none of
   them render black.
2. Check Max Stone, Natalia, and Albert separately; keep their exact source FRM
   portraits untouched.
3. Verify their 3D analog, outfit, centered framing, front-facing head view, and
   gameplay handoff.
4. Run both custom sexes through the public creator and into the cave.

Result: no black people-shaped placeholders, no lost original portraits, and
the selected character is the character that enters gameplay.

## 5. Finish Fallout 2's room and characters

1. Use the same world-space multiscale rock shader coordinates across all cave
   floor and wall meshes so the texture does not restart at component seams.
2. Tune the actual cave lighting, fog, floor relief, and wall relief together;
   stop using isolated preview lighting as evidence.
3. Put Narg, Mingan, Chitsa, and both custom sexes into that real environment.
4. Check skin joins, outfits, facing, grounding, idle, walk, turn, and stop in
   the cave.
5. Preserve the three exact original panels and finish their distinct 3D analogs
   instead of presenting one repeated sex-default donor as final.

Result: FO2 has one contiguous room and five correctly bound character starts.

## 6. Run every route end to end

For each game:

1. Start at the normal menu.
2. Create or select the character through the visible controls.
3. Complete the opening handoff.
4. Move in the first playable scene.
5. Save, close the process, Continue, and confirm the same character, outfit,
   skin, facing, position, and current package/stage return.
6. Fix failures immediately in their real owner; do not add capture-only logic.

## 7. Rebuild the movie

1. FO1: three premades, both custom sexes, and first cave view.
2. FO2: three premades, both custom sexes, and the contiguous cave arrival.
3. FNV: correct Doc chair cue, a longer clear view of character creation, then
   the correct creator return. Skip the unrelated remainder of the opening.
4. FO3: corrected birth-room cue, creator, and first transition.
5. Keep every view centered, consistently scaled, free of black edges, and long
   enough to see the controls and actor behavior.
6. Review the finished master at full speed and frame-by-frame for skin seams,
   wrong outfits, backward actors, sliding, bad furniture use, off-center views,
   black portraits, and environment seams. Fix the runtime and re-record any
   failed section.

## Done

The work is done when the four normal game routes run correctly, survive cold
restore, and the final movie shows those same corrected routes without hiding or
staging around a broken actor.
