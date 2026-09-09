# Fallout 1 and Fallout 2: product and completion plan

## The intended experience

One launcher retains Fallout 1, Fallout 2, New Vegas and Fallout 3 independently.
Each campaign keeps its own owned installation, rules, story and save identity.
Hex, first person and OpenXR consume the same character and gameplay state.
Changing a view must never start a different campaign or replace the character.

Fallout 1 is the immediate implementation priority: make the whole hex world
visible from the original maps, then complete its ordinary gameplay and fill in
artistic detail. Fallout 2 remains present and uses the shared classic systems
with its own source data and behavior. New Vegas work remains preserved.

## Character identity and Envision

| Campaign | Default characters | Custom characters | Gameplay identity |
| --- | --- | --- | --- |
| Fallout 1 | Match Max, Natalia and Albert to the owned illustrated portraits, including face proportions, silhouette, hair and expression. | Render the actual editable 3D face as a filled, strongly illustrated green portrait. Dark ink contours, grouped highlights and dithering support the likeness. | The same appearance controls the portrait, live head, body, outfit and animation. |
| Fallout 2 | Match Narg, Mingan and Chitsa to the owned sculpted-looking character artwork. | Keep the sculpted 3D appearance visible while editing. | Carry the approved 3D appearance directly into the world; no required cartoon conversion. |

The original portrait FRMs are raster art. Their sculpted look does not supply
reusable mesh geometry. Faithful default characters require fitting or authoring
the actual 3D geometry and materials against those references. A generic donor
face with the correct name is not a completed likeness.

Reflectron uses the existing native NIF, FaceGen, hair, eyes and control owners.
Envision changes the rendering of that same live face. It does not generate a
second character image with a different identity. Wire projection is optional
and does not replace Fallout 1's illustrated portrait requirement.

An appearance draft stores the complete editable face plus both campaign and
appearance-source identities. The shared classic picker reads the original GCD
statistics and BIO/FRM references, supports custom stats and uses the approved
live face directly in its portrait area. Its saved character choice includes
the full profile and appearance. These drafts are separate from campaign
progression. Fallout 1's player session now consumes that exact saved choice and
persists it with hex position and live stats. Source script state, inventory,
combat, faithful premade 3D bodies and the custom body's visual acceptance remain
unfinished.

## Delivery sequence and what closes each step

1. **Keep every campaign reachable.** One manifest contract controls launcher
   cards and actual launch dispatch. A missing installation affects only its own
   game. Unavailable modes remain visible with their actual status. Verify real
   launches and isolated save paths, including switching between campaigns.
2. **Block out the source world.** Decode every owned MAP elevation, floor, PRO,
   FRM, placed object and script identity. Join source wall occupancy into closed
   shells; use source-backed positions for doors, scenery, corpses and creatures.
   Keep a visible list of missing resources and unsupported layouts. Browse all
   locations without hand-placing actors or inventing a successful scene.
3. **Restore character creation and identity.** Recover the original premades,
   SPECIAL, skills, traits, age, sex and biography. Fit each default likeness.
   Connect custom Reflectron edits to one player appearance and the two distinct
   campaign styles. Verify edit, view toggle, accept, world entry, save and cold
   reload with the same face, body and equipment.
4. **Make the hex world playable.** Bind movement, rotations, collision,
   multihex occupancy, source animations, doors, containers, exits and map
   transitions to authoritative state. Restore HUD, inventory, character sheet
   and Pip-Boy from owned art/fonts with live values and ordinary input.
5. **Restore complete combat and equipment.** Bind AP, turn order, targeting,
   range, line of sight, weapon modes, ammunition, reload, melee, damage,
   criticals, death, loot and AI. Reproduce the user's pistol, knife, reload and
   FPS clips as regressions, then test additional actors and encounters.
6. **Execute both campaigns.** Complete general INT VM behavior, event order,
   dialogue, barter, skills, quest variables, world-map travel, encounters,
   timers, companions, endings and persistent changes. Exercise normal paths
   and alternate outcomes. A map catalog or a single encounter cannot close this.
7. **Complete presentation.** Refine the cave shells, Vault 13 and other source
   architecture, props, appropriate character models, outfits, icons, effects,
   lighting, sound and movies. Match opening transitions to the live world.
   Replace temporary source sprites with accepted 3D representations while
   preserving source identity, placement, collision and gameplay state.
8. **Finish shared views and acceptance.** Complete first person and physical
   OpenXR against the same saves and owners. Run cold starts, long playthroughs,
   save/reload at intermediate points and cross-campaign launcher regressions.
   Compare behavior, sound, timing, UI and final pixels independently with retail.
   Only complete campaign coverage and ordinary play can support a completion claim.

## Verified state of this recovery

- The September 4 native rewrite removed the richer old classic consumers.
  Their first-party implementation remains available in Git history, and the
  user's earlier recordings establish specific behavior to recover.
- Launcher manifest v2 and campaign identity now agree with runtime dispatch.
  Wrong-game roots and unavailable classic FPS/VR requests are rejected.
  Older live-data registrations remain readable. The real launcher state report
  recognizes the user's existing Fallout 1, Fallout 2 and New Vegas installations;
  Fallout 3 remains separately selectable. Thirteen launcher tests pass, including
  actual Fallout 1/2 headless startup from the emitted arguments.
- Both games now use the shared source-map browser and world loader. The
  earlier Fallout 1 source-only build rendered all 117
  elevations across 72 maps. Thirty missing-art placements in ten map/elevation
  combinations remain visible failures. Joined wall shells passed closed-edge
  and nondegenerate-triangle checks. This is
  presentation coverage, not campaign execution or completed 3D art.
- Both classic worlds open the shared custom appearance studio through mouse
  input. Keyboard edits, acceptance, saving, reopening unchanged and returning
  to the world have been exercised. Fallout 1 defaults to illustrated; Fallout 2
  defaults to natural 3D. These checks do not establish original-character likeness.
- Both worlds also open the original character picker. All six owned premades
  decode from their campaign's GCD format and retain source portrait/biography
  identity. Ordinary selection, custom-name editing, Reflectron approval, return
  to the picker, Envision and cold character-choice restoration pass. The chosen
  profile and exact approved face are saved together. UI controls and typography
  remain adapted, not accepted as retail parity.
- Envision reuses the same actor and preserves complete face state through
  toggles and appearance restoration. Fallout 1 now consumes a character choice
  through a source-hex player with original idle/walk frames, a live original-art
  HUD, character sheet, stop and save/continue. Custom 3D bodies retain the approved
  face; ordinary visual handoff and faithful premade bodies remain unfinished.
- The ordinary cave presentation was rejected by the user. Its asymmetric floor
  projection, continuous ground, owned rock materials, 3D cave props and smoothed
  walls have code changes and a successful Debug build. Shared roofs, room-specific
  scenery bindings, source NPC idle/directional art and map/elevation transitions
  are also in the current code. Their final appearance
  is unverified. The user stopped computer control and audit/test work; continue
  implementation without restarting those activities. The older footage remains
  the target. Map scripts, starting inventory, Pip-Boy and combat remain open.

The offline gallery helper is preserved at
`local/classic-authoring/build_native_gallery.py`, outside product inputs.
The complete source checkpoint must pass the required repository gate before
publication; selected checks do not establish a full gate pass.

## Try the current recovery

Open the launcher and choose Fallout 1 or Fallout 2's hex preview. Use
**Choose character** for the original picker. **Customize character** opens
the editable profile; **Reflectron 2.0** edits its face. Accept in Reflectron to
see that face in the picker, then use **Save selection**. Reopening the picker
restores the selection. Fallout 1's **Envision** button changes the rendering of
the same face; Fallout 2 retains its natural 3D presentation. A registered owned
New Vegas installation supplies the current native appearance renderer.

Both games' map/elevation controls browse the source world. After saving a
character selection, **Begin** creates the movement session at V13ENT (Fallout 1)
or ARTEMPLE (Fallout 2). Click floor
hexes to walk, Space to stop, F5 to save and C for the live character sheet.
Home centers the camera. WASD pans, right-drag or Q/E orbits, the wheel zooms,
G toggles the grid, R toggles roofs, F1 shows help, and Escape opens the menu.
Source exit grids use the same player and save owner across maps/elevations.
Continue restores the movement session; earlier richer
saves remain untouched in their original slot. The launcher still labels this
an incomplete preview because source scripts, inventory/Pip-Boy and combat are
not yet connected.
