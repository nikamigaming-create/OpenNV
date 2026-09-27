# Nonfatal actor hit reactions

Health damage previously reached `FalloutReferenceWorld.DamageActor`, and lethal
hits activated the source ragdoll. Nonfatal hits only provoked combat. No owner
selected the authored hit IDLE or published its animation. A creature or NPC
could therefore keep its existing pose/action through a qualifying limb hit.

`FalloutHitReactionTree` finds hit-context branches from winning IDLE conditions
and the actor's skeleton directory. It evaluates ancestor and ordered child
conditions, including hit location, body-part actor values, activity and random
selection. Runtime code does not select a named NPC, location, editor ID or
hardcoded reaction filename. Original-plugin insertion order survives winning
overrides; unrelated general idles cannot become substitute reactions.

The source conditions typically require a depleted limb. Ordinary damage does
not automatically imply a stagger. The hit-location condition supplies the
[authored body-part index](https://geckwiki.com/index.php/GetHitLocation);
radial explosion damage supplies -1 rather than its damage collider's anatomy.
[forced reactions](https://geckwiki.com/index.php?title=GetForceHitReaction) are
an independent input and are not invented for ordinary shots.

The selected source KF temporarily owns the actor pose and root movement. The
current attack/reload transitions back to pursuit, with ammunition already spent
retained and no reload grant before completion. Source text keys dispatch through
the existing sound owner. Normal combat resumes after the finite reaction.
An active reaction retains its clock on repeated hits. That interruption policy,
blend timing, source replay delays, special forced power-attack triggers and
general hit-script events remain unmatched or unimplemented; this is not full
combat parity. Unsupported conditions/channels/clocks remain visible.

The reference owns the selected IDLE identity/hash, KF identity/hash, anatomical
part, elapsed phase, start-event state, pose and separate selection random stream.
V20 saves retain that state, and supported earlier schemas remain readable.
Cold playback restores the selected clip without selecting or rerolling it.
Death or an explicit placement change clears the living reaction owner.

Synthetic checks cover source override order, hit-context rejection, reached
unknown conditions, snapshot round trip and malformed clocks/poses. Native owned
NPC and gecko checks exercise ordinary damage, qualifying limb damage, visible
bone-pose change, repeated hits, fresh-skin cold resumption, completion and
subsequent source attacks, without a player object. These fixtures use a test
floor and synthetic hit inputs. Explosion damage rejects the ordinary anatomical
reaction even on an already crippled limb.

An ordinary flat run continued the genuine Primm checkpoint and used mouse firing
at the resident hostile. Healthy-limb hits rejected the source conditions; a later
arm hit selected the authored reaction and visibly bent the actor over with its
arm held. An F5 save retained the reaction at 1.311 seconds. Playback completed
and the hostile subsequently fired again. This verifies the repaired OpenNV path;
it is not a matched retail comparison.

The final Release export cold-continued that F5 save at exactly
1.3111111111111085 seconds, completed the selected reaction, then fired again.
Full repository checks and both final owned NPC/creature audits pass.
