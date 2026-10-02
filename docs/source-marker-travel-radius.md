# Source marker travel radius

NPC Travel packages with a near-reference PLDT location retain the winning
nonnegative radius in game units. The native actor passes it to the existing
owned NAVM planner. Travel ends on a reachable triangle's projection inside that
radius, with locomotion distance supplied by the owned KF accumulation channel.
An endpoint outside the radius fails, including a same-triangle projection onto
the wrong floor. No marker, route, actor transform or quest result is substituted.

Exact furniture approaches and dialogue locations retain their existing
contracts. Their nonzero-radius behavior remains visibly unowned. The diagnostic
travel state reports the source location, radius and projected endpoint.
Package completion consumes its existing arrival once.

Synthetic navigation checks cover reachable directed-edge approaches, refusal
outside the radius, same-triangle vertical refusal and valid raised-marker
projection. The selected owned native fixture reads CG01DadCloseDoor's radius
25, travels through its actual NAVM/KF owners, reaches an accepted endpoint and
completes its package once. Its own package result programs are empty, so the
fixture correctly retains stage 16. The existing zero-radius package-result
fixture still reaches its authored stage 16 result from stage 14.

The attached actor script separately declares OnPackageDone for this package.
That event selects the next quest stage; native arrival alone does not establish
its execution. The reference package-event bridge, subsequent stage programs,
cold actor travel, dynamic obstacle avoidance, turn blending and matched retail
timing remain unverified. These fixtures explicitly prepare their initial quest
state and are component evidence, not ordinary campaign progression.

Owned input stays read-only and frame recording remains off during the checks.
