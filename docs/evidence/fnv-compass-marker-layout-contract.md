# Compass marker group layout

The owned HUD declaration supplies the compass window, common icon-group
template and individual NPC, landmark and objective templates. Native HUD
initialization creates all three groups from the common icon-group template
and supplies these runtime positions in native UI coordinates:

| Group | Runtime group Y | Child offset |
| --- | ---: | --- |
| NPC ticks | 10 | Authored NPC Y remains -10 |
| Landmarks | 30 | Authored landmark offset retained |
| Quest objectives | 30 | Authored objective offset retained |

The separate authored NPC-group template has Y=5 but is not the group selected
by native initialization. Using that declaration without the runtime binding
places the NPC rectangle five UI units too high. The common group's authored
Y=5 similarly places landmarks and objectives 25 UI units too high.

The compass window's authored Y is 65 relative to the hit-points branch.
Consequently, the NPC rectangle starts at 65 + 10 - 10 = 65 relative to that
branch. Its authored 32 by 32 dimensions, child offset, source texture and
texture padding remain independent of the group placement. No additional
centering or art correction belongs in this vertical translation.

The native 960-unit vertical UI convention scales these differences with the
viewport. At 2160 pixels high, the five-unit NPC correction is 11.25 pixels and
the 25-unit landmark/objective correction is 56.25 pixels, before rasterization.

This contract is supported by the owned declarations, native initialization
semantics and bounded read-only observations of actual NPC-parent and other
compass groups. It qualifies group placement inputs. Matched final pixels,
layer ordering, marker admission and art sampling remain independent checks.
