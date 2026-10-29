# Product status

Updated 2026-10-28. User priority: **retail behavior and presentation through
Goodsprings to the Strip, sustained native 3840x2160 at 60 FPS indoors and
outdoors, and physical XR acceptance with a separate 144 Hz target**.
FO1 remains an incomplete classic preview with selected movement, inventory,
equipment, door and save flows. Complete campaign execution and 1:1 3D art are
unverified. See [current-work.md](current-work.md) for the active owner.

The merged baseline is PR #326, `874c56d8`, with its local/hosted checks passed.
Current branch `codex/creature-idle-completion` repairs finite creature idle
return, restores idle-marker reservation adoption and reads physical furniture
positions before ordinal permissions. Its fresh publication gate and selected owned checkpoint audit pass; hosted
checks await publication.

ExportRelease `d6367556-4a09-427b-ba2c-9dd38a633259` continued tutorial 20 and
reached tutorial 50 through rifle practice, hostile gecko kills and corpse Take
All. NVGeckoMeat 1 entered inventory; HP remained100. F5 generation 1 completed
without save/cleanup error at 06:31:32.972 UTC in cell 0e1ab6. This is the canonical
current checkpoint. The game closed normally, disposing source owners without
the former creature-retirement/logger failure.

Both formerly missing NPCs 104f03/104f0a publish after Continue; source raking
runs and retires without a missing-reservation error. The lawn-chair mask case
is grounded in actual FURN 153553/NIF data and a synthetic regression; the final
route saved without the prior blocker, but its physical chair entry is unverified.
Earlier integrated ordinary VATS selection/cancel and two hostile-gecko misses
completed real shots/ammunition/AP effects. Hit/damage and full VATS acceptance,
an ordinary complete Caravan match, retail pixels, native 4K60 and physical XR
remain unverified. Active SayTo saving and creature Find/Food remain explicit
missing owners. No campaign or parity completion is claimed.
Classic reconstruction is parked; FO3 and classic work are preserved. Earlier
merged feature checks below describe bounded evidence, not validation of every
current branch change. The user's physical FNV VR playtest remains failed.
See [implementation-plan.md](implementation-plan.md) for the active delivery plan.

| Area | Current state |
| --- | --- |
| Terminals, lockpicking and hacking | Accepted manual/Force lockpicking connects admitted keyless DOOR/CONT activation to input, unlock/XP and resident activation. Locked TERM activation connects owned puzzle generation, selection, lockout admission, XP/statistics and delayed contents. Unsupported ownership, scripts, histories, variants and effects remain explicit. Ordinary campaign, full presentation and XR acceptance remain open. |
| All skills | All 13 NV skill values have a shared player owner. This does not establish complete mechanics: progression, modifiers, dialogue, crafting, combat and access checks must work through ordinary play. The user explicitly requires every skill. |
| Flat UI lifetime | Loading tips now retain borrowed shared font atlases. Normal cold Continue, an exterior/interior/exterior door cycle and F5 pass without the former disposed-image HUD/message failure. HP/AP, reticle and activation text draw. Full runtime checks pass. |
| Flat interaction recovery | An aimed source failure no longer permanently disables HUD updates or later valid prompts. Merged PR #249 uses the local lock first, otherwise its linked side's actual lock. Source/key, dynamic mutation and both cold orders pass. Ordinary walking reaches the affected door with visible E Open / Door / Locked; E respects its real lock, F5 commits, and separate-process Continue, ordinary re-aiming and another F5 pass. Full local and hosted runtime checks pass. Lockpicking, locked traversal and retail parity remain open. |
| Flat dialogue camera | Shared speaker focus and furniture pitch reset pass owned camera checks, a fresh-process questionnaire visual check and the full runtime gate. Normal play completes the questionnaire, tag skills and traits, exits the couch and saves at stage 110. Dialogue zoom/timing and matched retail framing remain open. |
| Flat object handling | Grab uses source dynamic bodies, weight limits and bounded force. Merged PR #251 retains model/body identity, transform, velocities, sleep and wind state in v55. Ordinary Z moved the skull from shelf to floor; F5, separate-process Continue, visible restored floor placement and another F5 pass. Detached preparation does not replace a live save callback. Local and hosted runtime checks pass. Saving while holding an object and matched retail physics remain open. |
| FNV world | Exploration, interiors and some dialogue render in the user's recording. Missing vegetation/models/materials, physics and LOD behavior remain visible in diagnostics. No world/parity completion. |
| Population | Shared Sandbox owns NAVM wandering, activity duration/preferences and ambient conversation attempts. The current candidate completes both reached exterior NPC LooseRaking activities and resumes marker selection or Wander. Doc's bed approach saves. General furniture entry/exit, other activities, voiced exchanges, defensive combat and complete population behavior remain unverified. |
| Animation props | Earlier owned raking checks retained native bones, ANIO rake transforms, idle clocks and sound history across cold reconstruction. Current ordinary exterior raking reaches End and request retirement; F5 retains both marker request graphs. Cold Continue now restores both reached marker actors. Combat interruption and general independent object controllers remain open. |
| Inventory actions | Ordinary hotkey equip/draw and physical Stimpak Drop/Take pass. Repair, Mod, per-instance magazines and loaded-ammo/Take All conservation compile; their added regression cases pass the final gate and broader ordinary use remains unverified. |
| Caravan / VATS | Shared runtime state, input and menus are integrated and compile. A complete ordinary Caravan match remains untested. VATS targeting/cancel and two queued misses produced real shots/ammo/AP effects; hit/damage, cinematic and broader engagement coverage remain open. No retail or physical XR acceptance is claimed. |
| Creature patrol | PR #248 merged source linked routes, retained waits/motion, native walk clips and actor-scoped XPPA results. The Securitron walk's inherited Bethesda rotation layout is decoded. Ordinary play saves/cold-loads patrol, crosses the saved cemetery failure through source-prefix triangle regions, reaches home, changes its quest variable, waits and retires through POEA/POCA. Post-result F5, separate-process Continue without restart and another F5 pass. Native capsule, wall, wrong-floor, saved-facing and local/hosted runtime checks pass. Other patrol inputs, marker idles, duration holds, Sandbox, full Victor dialogue and visual/retail parity remain open. |
| Automatic greeting / actor lifetime | Source editor-position dialogue waits retain native approach state. Ordinary saloon entry, F5 while waiting, cold Continue, walking into Sunny's voiced greeting, Goodbye and another F5 pass. Source corpses skip living AI; detached diagnostics cannot change another owner's corpse capture admission. Full runtime gate and synthetic contracts pass. General Sandbox activity execution remains incomplete. |
| Doc Mitchell tutorial repair | Current source-query and HUD repair passes full healthy source-script execution, five authored messages, cold stage receipts and a native control-label/handoff audit. Ordinary New Game completes the Vigor Tester, questionnaire, skills/traits, final Doc dialogue, exterior traversal and F5 at stage 200. Final release cold Continue, pause/resume and F5 pass; the movement prompt is visually readable after ordinary loading. All optional lessons and retail parity remain open. |
| Flat tutorial | Ordinary saloon Continue, front-door traversal, Sunny's outdoor rifle handoff, R reload, three source tutorial target hits and the next lesson choice reach stage 25. Objective 10 completes; F5 succeeds. Following Sunny and hearing her first-well instruction reaches stage 35 and F5 completes. Placed PGRE/PMIS physical state now restores, allowing that checkpoint to Continue. Invalid linked patrols are skipped; the geckos then reach unimplemented Sandbox. One gecko acquired, pursued and killed the player with melee hits, opening death recovery. Ordinary rifle fire kills both first-well geckos, advances stage 50, transfers Gecko Meat through corpse loot and saves. The next travel marker is elevated above its NAVM floor; projection now permits the downward offset while retaining lateral/upward bounds. Sunny reaches the second well and completes travel; saving after accepting the choice passes. NPC prefetch now retains geometry without binding gameplay until area publication. Ordinary F5 succeeds with two prepared bodies, and again after crossing the cell boundary publishes both actors; their unsupported Sandbox errors remain visible. Separate-process Continue restores both NPC positions and Sunny's completed travel; another F5 and the full local runtime gate pass. PR #254 merged with hosted checks passing. Remaining wells, gathering/cooking and matched retail firing/impact parity remain open. |
| Combat | User rejects combat. Ordinary second-well rifle hits expose a shared enable-parent bug: rendering uses effective enabled state while five combat checks use the local flag, freezing pursuit and hit reactions for quest-enabled actors. The correction now produces ordinary gecko pursuit and repeated melee hits through player death, plus settler/gecko combat. The owned parent-enabled retaliation check and full local/hosted runtime gates pass; PR #255 is merged. Blended-terrain impacts now use a source-position vertex/texture-weight query shared by player and NPC shots. The owned terrain audit and an ordinary aimed LAND shot resolve without material errors; the full local runtime gate passes. Matched retail material results and cell-edge lookup remain unverified. Unique compatible attack-group binding now passes source deathclaw/coyote hit, range-miss and cold-clock fixtures. Ordinary deathclaw terrain encounter, embedded emitters and faction assistance remain open. |
| VR rig | Visible severe rifle hand/forearm deformation around source recording 32:10-32:48. Physical VR acceptance failed. |
| Launch/render | The D3D12 simulator candidate uses a render boundary for launcher entry and preserves stereo through process cleanup. Direct/launcher close checks report zero view-count/type errors and unchanged checkpoints. A separately identified experimental source template also clears the profile RID leak in synthetic/shared-path and owned direct/launcher checks; the official product engine still leaks it. Native API-hash verification and source-template promotion remain pending. Physical D3D12 white output, shader failures, continuous stereo and headset lifecycle acceptance remain open. |
| Saves | Current candidate passes canonical stage-200 exterior Continue and F5 retaining two marker continuations/request graphs. Actual Continue restores both reached marker actors; the current candidate also saves tutorial 50 combat/loot progress. Earlier coyote limb-reaction checks retained exact phase, injury and health across processes. The source held-phase interruption correction passes its gate; off-cell audio continuation, save stalls and physical XR persistence remain open. |
| Quest/scripts | Doc's source-driven opening reaches stage 200 and current-build exterior continuation. Broader campaign scripts, reputation, conversation conditions, triggers and actor-value/effect behavior still require connected gameplay coverage. |
| Audio/UI | Wrist page changes are visible; complete interaction/input release remains unaccepted. Radio broadcast scheduling is unbound. Audio mix/sync is not certified by this review. |
| Traversal | Cross-cell NPC/creature route owners, authored floor destinations and the prior Cheyenne saloon portal remain open. |
| Fast travel | Earlier bounded flat/XR travel worked. Final population and saving after travel remain unaccepted. |
| Performance | Source-owned DDS reuse, fixed native names, bounded particle reuse and reduced planning allocations are retained. An earlier uncapped 4K exterior sample measured median 13.09 ms and p99 24.23 ms, failing the 6.944 ms 144 Hz budget. It does not establish sustained native 4K60 across indoor/outdoor streaming and combat. Physical 144 Hz, loading/save stalls and headset acceptance remain open. |
| Builds | PR #325 passed its required gate and hosted checks. The unpublished branch has compiled integrated Debug/Release candidates and reached the gameplay results above, but its final mandatory publication gate is pending. |
| Content | Official ESM selection logged; installed loose/resource winners still need provenance review. Native extensions disabled; pristine vanilla is not established by plugin selection alone. |
| FO3 | Earlier rendered New Game reached birth stage 22. No accepted standalone FO3 VR release. |
| Mods/TTW | Deferred until standalone games work. |

No campaign completion, complete population/combat/weapon support, retail pixel
parity or physical-headset acceptance is claimed. Original owned files, saves
and the user's recording remain private and untouched.
