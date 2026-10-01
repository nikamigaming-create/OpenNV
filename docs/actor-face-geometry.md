# NPC face geometry matching

`MatchFaceGeometry` now changes authoritative NPC-base geometry. It uses the
source actor's current race, sex and coefficients, including the accepted player
appearance. Other instances and unloaded references of the target base see the
same change. Scripted player targets still fail visibly because their character
identity write transition is unbound.

Candidate NPCs exclude the player, match the source race/sex and prefer the
FaceGen-preset flag. With no marked candidate, selection uses the other matching
NPCs. Winning records retain first registration order. Display names use
case-sensitive byte ordering and the admitted native small/large partition tie
rules; combined TTW data contains duplicate names with different geometry.
Empty selection fails before mutation. The selector also supplies character
creation so the menu and matching command use the same preset order.
Read-only observation of the complete native list agrees with first registration
for all 4,220 NPCs in the matched vanilla/DLC graph. TTW supplies reordered plugin
files, so its ordering is read from that graph instead of borrowed from vanilla.

The signed integer percentage is not clamped. Matching adds the scaled
source-minus-first-preset displacement to current target coefficients, preserves
the target's affine geometry age and stores geometry relative to the target
race's male default. Texture coefficients stay with the target. Repeated commands
are additive; even a zero percentage performs the native race conversion and
invalidates resident appearance. Failed source commands retain their executed
prefix and latch instead of replaying an additive mutation.

Save v25 stores geometry with target/model NPC, RACE and owned CTL source hashes.
Restoration checks winning model ownership, finite coefficient dimensions and
hashes before committing any overrides. Captured and admitted coefficient arrays
do not alias their callers. Save v24 and earlier supported schemas still load;
legacy schemas cannot contain the new geometry state. Existing native actors
refresh their owned body/FaceGen while retaining actor, skeleton and animation
phase. Removing an override invalidates appearance and restores source geometry.

Synthetic contracts cover selection, signed/truncated percentage, affine age,
texture preservation, shared/unloaded scope, cold state, array ownership and
atomic source-drift rejection. An isolated owned native Dad fixture verifies
changed pixels, stable pose, cold pixels at a matched idle time and identical
source pixels after removal, without retaining frames. A fresh ordinary TTW
opening accepts the Hispanic female selection, executes four face commands,
completes twelve speech commands and reaches CG00 stage 80's trait menu.

After ordinary trait confirmation, code saves and reloads the reached birth state.
A paused resave preserves face overrides, quests, references and player package
clock exactly; MenuMode script clocks continue normally. Cold presentation also
reports an unbound authored ragdoll accumulation-root rotation, so this checkpoint
is not fully reusable. Dad/Dr. Li dialogue selection reports no eligible INFO in
the next source topic. Complete actor cold clocks, active template changes,
native floating-point/timing parity and matched retail/OpenXR presentation remain
unverified. These checks do not establish character-creation or campaign completion.
