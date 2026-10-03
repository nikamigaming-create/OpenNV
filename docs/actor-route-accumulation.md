# Source actor accumulation along native routes

Actor package travel, combat pursuit and flight consume the source KF's horizontal
accumulation through the shared native capsule movement owner. The accumulation
supplies a distance budget after source scale, speed and limb modifiers. The
current checked route waypoint supplies direction and bounds the displacement.
Body facing still advances through the source turning-speed owner.

Previously, applying accumulation through the changing body basis could curve
the capsule away from the cleared segment. A long step could also pass a corner;
radius-sized waypoint tolerance then skipped the remaining clearance point.
The follower now consumes waypoints within native safe-margin tolerance and
clamps each displacement to its current endpoint. Physics sliding, gravity and
the bounded step resolver still own the actual resulting body position. The
query never writes the actor's pose or replaces source collision.

A synthetic native obstacle check combines changing facing, large accumulation
steps and a bent route around a tall blocker. It reaches the endpoint on the
supported floor without changing the body during planning. The selected owned
Escort fixture passes acquisition, wait, cold wait, blocked-wall refusal,
arrival and once-only events through the original NAVM/KF/package inputs. That
fixture uses an isolated floor and target; ordinary campaign state is recorded
separately in [current work](current-work.md).

Exact retail turning/locomotion blending, foot contact, dynamic actor avoidance,
automatic door recovery and matched temporal/pixel behavior remain unaccepted.
