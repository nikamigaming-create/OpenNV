# Player steady idle clocks

Stationary retail observations of the base `mtidle` and equipped `2hraim`
groups show frequency 1 and the same elapsed increment as the animation
manager's simulation clock, despite an equipped weapon animation multiplier
of 1.1. That weapon multiplier does not scale these steady idle channels.

The player presentation owner therefore advances its steady idle clock by
simulation delta. Attack/reload action rates retain their separate owner.
No pose offsets, geometry, movement or transition-reset rules change here.

Retail also exposes distinct base-idle and aim phases. Independent layer phase
continuation and weapon-change reset rules remain unqualified; this correction
does not establish complete first-person animation parity. Private addresses,
runtime captures and owned animation data are excluded from this contract.
