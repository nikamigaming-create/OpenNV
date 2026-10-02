# Player youth appearance and modal traits

SetPCYoung accepts one signed integer and stores its nonzero boolean value on the
player. Private observation of the owned executable confirms retained state and
appearance refresh only when that value changes. Its
[documented appearance policy](https://geckwiki.com/index.php/SetPCYoung) chooses
the race's default hair and omits face attachments. It does not change the race,
scale or face coefficients. Child race, scale and toddler animation are separate
source commands. Private executable bytes and addresses stay outside the product.

FalloutScriptSession owns that flag across reference, result, stage, fallback quest
and pre-world execution. Its existing snapshot retains PlayerYoung; absent legacy
fields default false. Repeated assignments do not invalidate unchanged appearance.
The actor resolver reads the winning race's sex-specific DNAM default, including
a null bald default, and suppresses PNAM/HDPT attachments. Stored custom hair,
attachments and original face bytes remain available when the flag clears.
[RACE's source layout](https://tes5edit.github.io/fopdoc/FalloutNV/Records/RACE.html)
distinguishes DNAM default hair from the empty NAM2 marker.

The shared player presentation observes character/session appearance revisions
as well as equipment. Source changes rebuild existing first/third-person body
owners even when movement or modal input is disabled. Cold construction reads
restored policy; the revision itself is transient invalidation state. Flat and
OpenXR consume the same appearance. Existing body/weapon/animation errors remain
visible; a successfully decoded policy does not certify rendered parity.

RuntimeNativeTraitEntry now owns the same pause lifetime as name and race menus.
The owned menu keeps processing input while gameplay is paused. Acceptance releases
the pause before further source progression; release is idempotent, tree exit
cleans up, and a previously paused tree remains paused. This does not change the
source ordering of ShowTraitMenu or substitute a different screen.

Synthetic contracts check signed flags, unchanged revisions, malformed arguments
before their suffix, fallback/reference/bootstrap execution, cold restoration,
legacy clearing, winning DNAM overrides, bald defaults, attachment suppression and
recovery of unchanged custom choices. Owned native checks construct male/female
first/third-person bodies and restore the original policy. The existing rendered
trait fixture now verifies paused gameplay clocks, ordinary pointer/keyboard
input, Done/resume, previous-pause preservation and exit cleanup. Frames are not
recorded by these fixtures.

Fresh ordinary flat New/Capital input reaches CG00's owned trait screen after
twelve completed speeches. Speech state, camera-package time, quest progress and
the trait draft remain identical across an extended hold. Done releases gameplay;
seventeen speeches complete, source sound stopping removes three birth loops,
the camera package clears, Dad disables and CG00 stops. SetPCYoung executes and
the [source sound-path owner](source-sound-paths.md) executes SetSoundSourceFile.
CG01 stages 0 and 5 are entered. The later [toddler/scale owner](player-toddler-animation.md)
executes the source movie and actual playroom transfer. The session quits
normally with readers drained.
State-publication overruns, missing references, actor behavior and cold-animation
gaps remain visible. Toddler quest/Vault completion and matched retail/XR timing
or pixels are unverified.
