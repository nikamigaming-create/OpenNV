# Fallout 1 character creation and opening contract

Date: 2026-08-25

This promotes one uninterrupted private local route: a functional asset-free
OpenNV main-menu adaptation, the owned original three-premade-plus-Custom
picker, complete character creation, the owned original Overseer briefing, a fade into live first-person control at the
authored V13ENT first-run state, a first-person Vault look-back, continuous FPS
movement/shooting over the source walk mask, shoulder orbit with exact
centered-hex commands, and tactical movement with two turn-based giant-rat
kills in the same session. The complete showcase kills a third rat in FPS.

## Owned source boundary

- Original Fallout `MASTER.DAT` SHA-256:
  `a79090e035e33c178aee23fe72f7ef0a5f3be76f733c252522983aad22f87364`
- Original creator `ART/INTRFACE/EDTRCRTE.FRM` SHA-256:
  `48657057b1e40674a7882623ce7b8726d42e03a59bdbe50c9da95810bdc12258`
- Original picker `ART/INTRFACE/PICKCHAR.FRM` SHA-256:
  `d6c10612a915c4d55f0e72c961341339ffa0d56bc03d87e2e156379946cd80dd`
- Original `COMBAT.GCD`, `STEALTH.GCD`, and `DIPLOMAT.GCD` SHA-256:
  `31b9416a1f75e78860a510bca7c40fc02e8123145bf219c35eeb2c8394725ce1`,
  `516062ae8e12615a0f65af441c23d3ec2b44e8a89e7c94081542be388fc4d5a2`, and
  `5abd44aae8befda02ab69d68646ec7179642355bb0e062f6ca1f3f92d5f4340e`
- Original Pip-Boy 2000 `ART/INTRFACE/PIP.FRM` SHA-256:
  `332ba72b181f854d529b38e7e76fd9414ad9e9cb317dbb859176b2c3cdfca006`
- Original `ART/CUTS/OVRINTRO.MVE` SHA-256:
  `feb5c0a35687de321519602f6b62c132dbee8b9b59b3031c491666933e6ad3c3`
- English opening text/timing SHA-256:
  `b8f4c7b1c31e9ccea83ece47821ce62be5ff3c0b5d7f266f68ea473e017eda80` /
  `5151c422828b30062c4e2acab042ed639d492bd338d95b18f4c856f2c5d46e09`
- Official manual SHA-256:
  `9a93ba6c8a430e7d8cfffa59acd3aa8677eb9b591d0e1653d5030280bb6dd330`

`dat1_archive.py` reads the owned DAT1 directory and LZSS members directly.
`prepare_fo1_character_start.py` writes only an ignored private cache. The
commercial MVE, decoded UI PNG, JPEG playback frames, audio, and final video are
never public package inputs.

The local Interplay decoder reports one damaged motion-offset packet late in
the movie. The source MVE is retained unchanged and hash-pinned; playback
repeats the last clean decoded frame for derivative frames 1348-1353. This
bounded six-frame repair is recorded in `character-start.json`.

## Character contract

The route starts with exact owned 640x480 picker chrome and exposes Max Stone,
Natalia, Albert, and Custom. The three premades are parsed from their exact GCD
records and retain their owned portraits, biographies, age, sex, seven SPECIAL
values, three tagged skills, and two traits. `Take` accepts one unchanged;
`Modify` loads it into the full editor; `Create Character` opens Custom.

The editor uses the exact owned 640x480 creator chrome and implements name, age
16-35, sex, seven SPECIAL values, five spendable SPECIAL points, all 18 skills,
exactly three tagged skills, all 16 traits, and up to two traits. The proof
character is:

- `NIKAMI`, male, age 25
- ST 6, PE 7, EN 5, CH 5, IN 5, AG 7, LK 5; allocated total 40
- tagged Small Guns, First Aid, and Speech
- Fast Shot and Bloody Mess
- derived HP 31, AP 8, AC 7, Sequence 14, Small Guns 62%
- Fast Shot changes the live 10mm attack cost from 5 AP to 4 AP

The selected profile is applied to the authoritative tactical session and is
present in the save/report; this is not a decorative front end.

## Opening and map handoff

The original owned 432x320/15-fps Overseer frames and stereo soundtrack play
for 112.167891 seconds. The flow then hands off to `V13ENT`, elevation 0, tile
`17690`, rotation 2. The mapped Vault door remains at tile `16290`. The video
then shows continuous FPS movement without tactical AP, two FPS hits and one
rat kill, shoulder orbit and exact-center movement, a fade into the shared
tactical state, two 4-AP 10mm attacks, two more source-rat deaths, and a wide
tactical map tour.

The final visible owned frame fades to black. While black, the single gameplay
camera is prepared at the exact source-backed spawn with a 1.66 m eye height,
68-degree FOV, and horizontal forward vector aligned from the door into the
cave. Black then lifts directly into live first-person control; there is no
duplicate cinematic camera and no invented threshold walk. `SKIP` and `Escape`
converge on the same first-person state.

The door remains open at first control so a 180-degree look-back can show the
owned cave-to-Vault frame, gear leaf, airlock, hall, lit corridor, and
depth-tested `13` sign. This open live-control door state is an explicit
presentation adaptation, not a retail door-state parity claim. Continuous FPS
movement stays on the source walk mask. A black-frame-safe switch then snaps to
the nearest valid authoritative center and reveals the same player, cave, rats,
HP, AP, and path in tactical projection.

## Current source-composed gameplay HUD evidence

Private evidence: `fo1-classic-hud-source-composed-20260826-r1`.

- owned character-start manifest SHA-256:
  `482f298c36bd4f0213c5ad9be2ea930fe899adab0af91828adb93a2da2335647`
- source coverage: hash-pinned `IFACE`, `NUMBERS`, AP lamps, combat curtain and
  lights, end buttons, item-action panel, 10mm Pistol inventory art, six main
  button faces, and `FONT1.AAF`
- source layout: one 640×100 RGBA compositor at original coordinates; the
  runtime report records `godotLabels: 0` for the gameplay HUD and the exact
  41×19 PIP hit area at `(526,78)`
- palette correction: valid per-entry six-bit COLOR.PAL channels are expanded
  to eight-bit even though the retail palette contains invalid sentinel entries;
  the message font resolves `colorTable[992]` to RGB `(60,248,0)`
- live state: original numeric strips render HP and AC, original 5×5 lamps
  render AP, the item panel renders SINGLE/AP cost/pistol art, and the original
  final `ENDANIM` frame opens the tactical TURN/CMBT curtain
- native demo schema/status: `opennv-fo1-new-game-demo/v5` / `pass`; 1,970
  1280×720 frames at 30 fps, 65.666667 seconds; three kills and two tactical
  attacks; native AVI SHA-256:
  `0b3fdc52993552150576d8832186bf1212fcdb935e5c7852f6c6c3a1bc41b747`
- mobile proof: H.264 Main + AAC-LC, 854×480, limited-range BT.709 `yuv420p`,
  fast-started, 49 seconds, 1,926,837 bytes, SHA-256
  `061f31a9ae0a6e7b1b3c8c2a2e1d64836d4dc80964b3d2539b24ba534a5fef38`
- demo report SHA-256:
  `f9cf4d323f11949d36730d7edc42a5c7ba8ca9915fcd6bc3f12563b9ce748b85`
- Windows app control, foreground activation, and injected input: all `false`

## Prior picker, Pip-Boy, FPS, and wall-closure evidence

Private scene: `fo1-v13ent-hex-20260825-r50`; scene SHA-256
`4263c1710cca7a326638d55bb2e3b250646c3a9edc4d318e8e7b4bd933e40976`.

- new-game report schema/status: `opennv-fo1-new-game-demo/v4` / `pass`
- picker: Max Stone, Natalia, Albert, and Custom; full editor coverage retained
- Pip-Boy: owned original Pip-Boy 2000 chrome; live Status, Automaps, Archives;
  one clean proof open/close
- movement: continuous source-walk-mask FPS with no tactical AP, exact-center
  shoulder commands, then exact centered-hex tactical movement; final center
  error `0 m`
- cave: wider/taller overlapping source wall ribbon closes the prior clear-color
  wedges; non-uniform local volumetric fog remains; FPS disables tactical melt
- input/visibility: conventional non-inverted FPS mouse look proved; source 2.5D
  cards are forced off in FPS and the continuous 3D floor remains
- grounding: 114 cave props AABB-seated; maximum seat depth `0.10202604 m` and
  maximum placement error `0.000000022351742 m`
- combat: two FPS hits kill one source rat; two turn-based attacks kill two more;
  player alive
- phone MP4: H.264 High + AAC-LC, yuv420p TV range, 854x480, 72.933333
  seconds, 3,678,915 bytes, SHA-256
  `44ce33f6083225782486bfee399bcf6ad569304d937c7451876e168cd80ee834`
- full 720p MP4 SHA-256:
  `e1b840472bfcfcba0e0806a870a4527bb485d4bd06fb2c28fe61521dbc84b462`
- new-game report SHA-256:
  `52f96a8dc8ab7222cdb78c24e2e0e867ba97d53ffbcc89914b127c43c7f1515d`
- Windows app control, foreground activation, and injected input: all `false`

## Complete ranged and melee combat showcase (2026-08-27)

Private evidence: `fo1-complete-combat-showcase-20260827-r2`.

- report schema/status: `opennv-fo1-new-game-demo/v6` / `pass`
- one shared-state sequence: FPS pistol miss and environment impact, FPS pistol
  kill, FPS knife kill, exact-center shoulder movement, tactical pistol kill,
  source-capacity reload, tactical knife approach/kill, and tactical map tour
- source-bound starting combat data: 10mm Pistol PID `8`, Knife PID `4`, 10mm
  ammunition PID `29`; the selected Fast Shot character pays `4 AP` for the
  pistol and finishes with `12` loaded / `68` reserve rounds
- result: four distinct source rats killed; FPS kills `2`; ranged attempts/hits
  `4/2`; melee attempts/hits `5/5`; reloads `1`; player alive
- presentation proof: tracers `4`, impacts `4`, casings `4`, grounded casings
  `4`, ricochets `1`, melee sweeps `5`, audio events `31`; all four corpses have
  ground error `0.000000013969839 m`
- native master: MJPEG + stereo PCM, 1280x720, 30 fps, 2,950 frames,
  98.333333 seconds, 300,092,972 bytes, SHA-256
  `0adc525ab27039ee6dfc952b9024677640a96b9c980b85c8d84c812c6ae396b0`
- phone delivery: H.264 Main + AAC-LC, limited-range BT.709 `yuv420p`, 854x480,
  98.333333 seconds, 3,200,372 bytes, SHA-256
  `6d5efd274edfab6464278f1a907849844ae8a74309447429a077130fa76174ba`
- report/save SHA-256:
  `1787d0756d8da9c358a9b6e76c4311473421c447dd3d20a4ff5918744e25e829` /
  `956052f483b9e711936dd6aba5b6730df94f80120fe9ea84f82cc1d083f67f4b`
- overall/FPS/tactical QA-sheet SHA-256:
  `130c5da120107da3f8cd00bbdbd3d025ef2a4f9451d26d86209be38e157f4235` /
  `2eece907413c2035ee0924966078d9e7d6e4f50acc01b998cb1e378c4698f20b` /
  `4cbd16c5dcff92e2f46de33f262c46f86214d1a68e4f19e81ceade2149fef282`
- Windows app control, foreground activation, and injected input: all `false`

## Current user-supplied visual baseline (2026-08-27)

Private evidence: `Fallout1-3D-HUD-WEAPON-FIX-MOBILE.mp4`.

- H.264 Main + stereo AAC-LC, 854x480, 30 fps, limited-range BT.709
  `yuv420p`; 3,088 frames, 102.933333 seconds, 3,399,103 bytes
- SHA-256:
  `6a917fc1e10219527d1d16c5f67b723ec64b63dc3efc0986a4a7e29b5ca3f546`
- visibly covers the NIKAMI character editor, live first-person cave traversal,
  10mm and knife HUD states, third-person shoulder presentation, AP combat,
  and the 3D hex-tactical grid in an apparently continuous sequence
- it does not show the Pip-Boy open, so it is not Pip-Boy visual evidence even
  though the deterministic demo reports one programmatic open/close

This recording is the current visual regression baseline supplied by the user;
it is not committed or packaged. Its source state/report identity has not been
recovered, so its renderer is also unproven and it does not supersede the
hash-bound `v6` promotion above.

## GL launcher recovery acceptance (2026-08-28)

The ordinary desktop launcher was visually followed through Fallout 1 Hex,
the OpenNV Fallout menu, the owned Max Stone picker, the owned Overseer movie,
the visible Skip action, the black-frame handoff, and live V13ENT Hex gameplay.
The same registered cache was then run under an explicit
`--rendering-method gl_compatibility` deterministic `v6` sequence:

- exit code `0`; report schema/status `opennv-fo1-new-game-demo/v6` / `pass`
- report/save SHA-256:
  `9f973f37241b8c636f2046a51f0340de450b4926abf845474147b78de40dbd1e` /
  `3b666ccfda629f7602556a63f6138f9a9492f09c3e55dc7e2e6986607315eb65`
- exact sequence includes first-person Vault look-back and movement, FPS pistol
  and knife kills, third-person shoulder movement, tactical pistol and knife
  kills, one reload, and the wide tactical map tour
- result: four kills, two FPS kills, three ranged attempts/two hits, four melee
  attempts/four hits, one reload, player alive

This closes functional Hex/FPS/shoulder admission for the compatibility
renderer. Godot reports that volumetric fog and fog-volume shaders are
unsupported under GL, so the recovery is not visual parity with the supplied
baseline or the prior Forward+ captures. The transient report/save remained
private and were removed after hashing.

## First-person final-frame handoff evidence

Private evidence: `fo1-v13ent-first-person-opening-20260825-r10-watched`.
Final phone delivery: `fo1-v13ent-first-person-opening-20260825-r11-mobile`.

- demo schema/status: `opennv-fo1-new-game-demo/v2` / `pass`
- owned movie mode/scale: `watched` / `1`
- rendered owned frames: `1639`; handoff frame index: `1638`
- handoff JPEG SHA-256:
  `3926a023e1c8c5c846a2fb4a5023de435ddc939fa55f7716d845f8211b4fc2a1`
- source handoff: V13ENT tile `17690`, elevation `0`, rotation `2`; door tile
  `16290`
- measured spawn error: `0 m`; camera-position seam: `0 m`; horizontal cave
  alignment: `0.99999994`; pre-control/live forward alignment: `1.0`
- first-person presentation: `1.66 m` eye height, `68°` FOV, local player mesh
  suppressed; exact-hex movement remains authoritative
- native capture: 4,589 frames, 1280×720, 30 fps, 152.966667 seconds,
  347,399,996 bytes, MJPEG + stereo PCM, SHA-256
  `ff5efd6599204ae22408fbd3a793b2a376dc8b1e9901e929ce4f90aa584342fc`
- 24-second handoff/first-person/combat phone MP4: H.264 Constrained Baseline +
  AAC-LC, 854×480, limited-range BT.709 `yuv420p`, 3,099,414 bytes, SHA-256
  `faf1becfc4952f0887343fe50dd4d41bd520dc1dfea0b44d3906434b99261480`
- full watched phone MP4: H.264 Constrained Baseline + AAC-LC, 854×480,
  limited-range BT.709 `yuv420p`, 6,879,256 bytes, SHA-256
  `3fec35bbd2d847ef47c494b9d4793e0ab71a31b1f9358234856040d08a7ba30b`
- demo report SHA-256:
  `875444f08ec5a55e8915a331f6d19c9f4fa59a6837d6fe8ccac4eb49d2e2b984`
- phone contact sheet SHA-256:
  `d3ebfe54a0e858e5f2464e836909cf151e22a7a7c333f9c17b482363b52bf988`
- final tactical/first-person contract report SHA-256:
  `2d9289caa8ab55a768e17bab3da8c4052349844c06af9b6e277fbef00d5faafa`
- Windows app control, foreground activation, and injected input: all `false`

## Prior native evidence (superseded presentation)

Private evidence: `fo1-new-game-proof-video-20260824-r1`.

- demo schema/status: `opennv-fo1-new-game-demo/v1` / `pass`
- full-speed opening playback scale: `1`
- native capture: 4,350 frames, 1280x720, 30 fps, 145.0 seconds
- native AVI: MJPEG + stereo PCM, SHA-256
  `7852c5f33b2cf3ab9359377621cabad0fc55d9f07c4f38717c7b1d94f2f28000`
- delivery MP4: H.264 + stereo AAC, SHA-256
  `98ebb032e0587bfb66d5b3122bd88f2cacd4dc93deb2309c163f85d97bda866d`
- demo report SHA-256:
  `d66eb73cf996b6bc940fc00146d569d462b26cd2e84421b245a90a02f482dd5a`
- contact sheet SHA-256:
  `a9d341303f7cd9ae465770f58226c4ffd48880663cbfe1ce3e3aef2b0a191e7f`
- Windows app control, foreground activation, and injected input: all `false`

## Prior skip-route and mobile evidence (superseded presentation)

Private evidence: `fo1-v13ent-skip-landing-video-20260825-r16`.

- demo schema/status: `opennv-fo1-new-game-demo/v1` / `pass`
- skip mode: one opening frame, playback scale `0`, same landing transition
- mapped handoff: tile `17690`, rotation `2`, door tile `16290`; door closed at
  control handoff
- movement: four exact hexes in third-person perspective over the same
  `Fo1TacticalSession`; Fast Shot retains the live 4-AP 10mm cost
- combat: two attacks, two distinct source rats killed, both corpses visible
  and grounded, player alive
- native Vulkan capture: 1,201 frames, 1280×720, 30 fps, 40.033333 seconds,
  SHA-256
  `5409b6858cee27794c3969b6718fb8544423716ae4545752ca56a4ba93ab7aad`
- full 360p phone MP4: 443,105 bytes, SHA-256
  `870d4528936e99cceced5c2cc28f6ab3447b2f503da9b50aea31871be7534839`
- full 480p mobile MP4: 887,073 bytes, SHA-256
  `10fb6d07463c8b86d5b5809045e2606f6df727e92110b6663be25aeb0671c0ac`
- landing-only 480p MP4: 319,645 bytes, SHA-256
  `8158d530a05d776dc2c57d86ee8c4933de978b49f1f0546e8b9ff2210fcedc8c`
- third-person/combat 480p MP4: 565,175 bytes, SHA-256
  `e939848ff87aa6709e942fcd6d63946318fb2a8a9338fafbf0b817ef762cb41c`
- all delivery files are H.264 Main, yuv420p, TV range, AAC, and fast-started;
  Windows app control, foreground activation, and injected input are `false`

## Parity verdict and remaining boundary

Source-bound and functionally matched: original picker/premade records and
portraits, creator background, creation constraints used by this profile,
Pip-Boy 2000 chrome, original opening images/audio, first-run entry
tile/rotation, map hexes, and the selected character's live derived values.
The preceding New Game/Exit menu is an asset-free OpenNV adaptation and is not
claimed as the original Fallout menu or startup-logo presentation.

The gameplay HUD is now source-composed and no longer uses Godot labels.
Retail pixel/timing parity is still not claimed for character-creator and
Pip-Boy live text, button-down animation, the cursor, click sounds, or automated
selection cadence. Complete runtime effects for every trait, retail
to-hit/critical/armor tables, broader item/container/equipment behavior,
dialogue, quests, every map, a campaign-complete FPS combat implementation, and
OpenXR remain unpromoted. The bounded V13ENT slice does parse its exact starting
inventory and provides shared FPS/tactical pistol ammunition and reload, knife
and pistol attacks, continuous FPS locomotion, deaths, and save state. The final
owned movie frame and live donor-asset cave are intentionally separated by a
fade and are not claimed as a pixel-matched geometric dissolve. The owned
New Vegas cave, actor, door, and rat assets are private 3D presentation donors;
they are not claimed as Fallout 1-authored 3D equivalents.
