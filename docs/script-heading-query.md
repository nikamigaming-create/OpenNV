# Actor heading queries

The shared `GetHeadingAngle` function reads the calling actor's actual horizontal
placement and facing and the target reference's actual placement. Source XY is
horizontal, +Y is forward at zero heading, and positive source Z rotation is
clockwise. The result wraps into -180..180 degrees; target height is irrelevant.
Nonactor callers return zero. Missing placement, unrelated world spaces or a
missing spatial owner remain visible failures before the consuming local write.

Reference events, result scripts and shared or standalone quest execution use
the same query. Typed reference arguments preserve compiled source identities.
An unprefixed quest query retains its nonactor caller and returns zero.
Native callbacks prefer the existing presented roots and retain the shared
placement/motion fallback; they do not materialize or move a target. Shared
motion placement includes the retained actor rotation as well as position.

Synthetic checks cover signs, facing, wrap, horizontal height independence,
repeated contact, typed reference receivers, both quest execution owners and
missing-owner prefix preservation. The selected original TTW photo-trigger audit
executes its unchanged compiled script with isolated actor-ready inputs and
bearing cases inside and outside 45 degrees. It also checks contact departure
and cold restoration of the trigger local. That fixture does not establish
physical contact, campaign advancement or retail parity.

The actual user kill reached CG02:80. Both original actor-ready bits were set,
and the original photo trigger fired, then stopped at its missing heading
expression before setting PlayerReady. This query supplies that general engine
capability; the live CLR must use the repaired implementation before native
advancement can be verified.

NPC construction now attaches its combat owner before AI package selection.
Source GetInCombat conditions therefore have their owner while selecting and
restoring the saved package animation. A missing owner previously interrupted
Paul's chair selection and subsequently compared the saved chair clock with a
fallback standing idle. This ordering repair requires a native cold check;
legacy Travel root/motion persistence remains a separate open capability.

Source behavior: [GECK GetHeadingAngle](https://geckwiki.com/index.php/GetHeadingAngle).
