# Current work

## Active objective

Make ordinary loading, saving and play reliable in standalone FNV, standalone
Fallout 3, TTW and the combined stack, verified independently in flat/OpenXR.
Repair reached physics, actors, vegetation, LOD and performance, then deliver the
requested loading/walking showcases for all three games. Standalone FO3 must
reach the exterior and look toward Megaton through ordinary saved play.
Preserve the complete TTW Megaton/Union Station/Mojave/Benny route, JAM,
compiled SCDA, unchanged x86 plugins and retail comparison requirements in
[implementation-plan.md](implementation-plan.md). All 36 broad requirements
remain open; component checks do not establish a completion percentage.

## Verified state

FNV's ordinary creation now reaches stage 200, accepts the source farewell and
Hardcore choice and crosses the actual house door into Goodsprings. An ordinary
menu save writes a complete v48 checkpoint at 120 HP with 27 inventory entries
and CharacterCreationComplete=true. Another process cold-Continues that exterior
checkpoint in 35.7 seconds with the original position, 120 HP, all 27 entries and
unchanged save bytes. Its requested daytime loading/walk/pan MP4 is delivered.
The earlier stage-110 checkpoint cold-restores 120 HP with unchanged bytes.
The earlier stage-55 v47/v48 checkpoints also cold-restore in flat; native OpenXR
simulator controller input saves and another process cold-restores that partial
opening. Physical-headset acceptance remains pending.

The reported FNV runaway globes now bind their actual source model hinges.
The previous constrained-body static substitution has been removed. Separate
source checks pass the globe/bucket frames in FNV, FO3, TTW and the combined stack.
Native independent instances fall, rotate about their declared axis and settle
without driving their sibling or changing their prototype. The rebuilt ordinary
room retains both globes on their stands with the spinning stopped; inspected
pixels and source-bound velocities agree. Recording is off and temporary frames
are deleted. See [source model hinges](source-model-hinges.md) for boundaries.

Standalone FO3 completes ordinary CG00 creation and reaches CG01 toddler stage
14 by walking to Dad and opening the source playpen gate. Its independent manual
v48 checkpoint cold-Continues in another process at 100 HP, the walked position
and both inventory entries with unchanged bytes. Inspected pixels show Dr. Li
and Dad's body/outfit; the red placement-marker meshes are excluded. Source
executable defaults, creation menus and activation labels bind independently of
FNV. Matched retail color/final pixels and the complete opening remain unverified.

The shared finer capsule grid now lets Dad complete his original close-gate
package and advance stage 16. His room door declares that NPC's base as owner;
the native interaction admits him without unlocking it for another caller. Dad
opens it once, walks through, and original completion scripts advance stages 18
and 20. Ordinary player movement leaves the playpen and reaches stage 30. The
later state has no new cold checkpoint: manual saves fail on active background
radio speech. The immutable stage-14 checkpoint remains reusable. See
[native NPC door access](native-npc-door-access.md).

TTW ordinary input completes the aggressive/key route, defeats the guards,
loots the source office key/password and crosses the authored terminal, tunnel
and vault exit to stage 150. The complete cave, post-loot and exterior checkpoints
remain immutable and cold-verified at 115 HP and 8/14 rounds. Native exterior
cold Continue drains the original selected LOD queue: all 121 selected tiles
remain resident, pending zero. A subsequent walked v48 checkpoint retains nine
inventory entries. Another process cold-Continues it in 43.1 seconds at the walked
position with 115 HP, 8/14 rounds, nine entries and unchanged bytes. The requested TTW
loading/short-walk MP4 is delivered with actual game time, audio and stalls.
Megaton/train/Mojave traversal and physical-headset acceptance remain open.

Atomic flushed same-folder saves and transactional load rollback preserve
Continue on failed writes/restores. Source PCM loop position/release state,
automatic object clocks, door groups and ended player-package/radio histories
retain their independent owners. Contract and native fractional-sample/pause
checks pass; active speech/opaque callback continuation still blocks saving.
The finite authored image-space whiteout expires rather than accumulating.

## Current divergence owners

Active background radio speech blocks an ordinary FO3 save at the reached stage
30. Its current voice, response/link/result cursor, generation, source/media
identity and audio sample clock need persistent owners. Ended speech/radio
history cannot substitute for active continuation. Preserve opaque-callback
refusals while implementing the reached source-owned speech capability. The next
ordinary tutorial target is source SPECIAL book 02ecc0, after a reusable save.

The reached TTW exterior has 565 missing owned SpeedTree references. Paths now
resolve their original trees/ resources; procedural geometry decoding remains
unbound. Terrain/object LOD have independent coverage, and nearby fading needs
the source distant flag plus actual object coverage. Property-free material draws,
water/degenerate semantics and whole-reference upload spikes remain open. FNV's
reached Goodsprings view reports 16 missing material draws despite its drained
LOD queue. The original FNV/TTW reference-script failures remain visible.

Generic loose-object physics poses have no complete persistent owner yet.
Source model hinges fix runaway motion, not save/retail solver parity. Other
joint kinds and nonuniform scale remain explicit unsupported behavior.

Combined-stack ordinary play/save acceptance, independent FO3 XR acceptance,
active speech saving, modeled emergency lights, complete packages/IDLE,
GetDetected, Flee/NPC portals, level allocation and complete world rendering
remain open. JAM source hit/calendar contracts do not establish native callback
or unchanged-DLL behavior. No matched retail parity or clean-system complete
New Game/campaign claim is accepted from these component checks.

## Next owner and outcome

Primary owns serial builds, live input, save verification and checked publication.
The bounded source-hinge fix is merged through the required runtime gate and
checked PR. Publish the bounded native NPC route/ownership repair, implement the
reached active-speech save owner, and continue the genuine FO3 opening toward the vault exit/Megaton,
and finish the requested FO3 video from a cold save. Keep vegetation decoding and
upload timing active, complete the independent flat/OpenXR stack matrix and
continue the authored TTW train/Mojave/Benny route without bypassing conditions.

## Private continuation

Owned files, saves, captures, helpers and logs remain private and out of publication.
Standalone FNV run: tmp/development-lab/loading-saving-fnv-20261007.
Goodsprings v48 slot: 100c5809e3aa4af5b4fe7801cfbd954a, cold verified.
SHA256: A4F3B58394F7326A56D7E96821DAB2A79A77FC7996C07B98ECEE26B5011A6C3A.
Earlier stage-110 slot: 356328b8de2046d989d7aa39db85c799, cold verified.
SHA256: 6D858F2A980247BE14EB352C781A929C92FF9E2CFC366990ACA672D570E06528.

Standalone FO3 run: tmp/development-lab/loading-saving-fo3-20261007.
Stage-14 v48 slot: 5341c6a0d9f146ddb7f141a4b1237961, cold verified.
SHA256: 340C3CC918A528BBCE18FE18F276C4D3CD8F81C5F3965F2922DF46845BEAC012.

TTW run: tmp/development-lab/loading-saving-ttw-20261007.
Walked v48 slot: f657f146263a477a889668cc57b6f136, latest cold verified.
SHA256: 99A3A0D51E88DB36B7D35030AB45FC2FA8E825EC5E5102EF930DE35472D930F0.
Prior cold-verified exterior slot: 5e24f48be6604a26b50c52e81c8715e7.
SHA256: D9654D35DB7BB3CD3731ED5C4F72E06DA985D62D07665A7CA6924D8545F8F13B.
Requested clips: local/recordings/showcase-20261007/ttw-loading-walk-01.mp4 and
local/recordings/showcase-20261007/fnv-loading-walk-01.mp4.
Preserve original saves and selected active diagnostics. Temporary recording
frames are deleted; recording remains off outside a specific visual check.
