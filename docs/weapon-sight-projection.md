# Weapon sight projection

The winning WEAP DNAM sight angle at offset28 and independent first/third-person
animation exclusions select flat firearm aiming. The exclusions suppress only
the corresponding `aimis` group; they do not suppress source camera zoom. Zero
disables firearm aiming, and GECK's1..5/75 no-zoom values retain the world view.
Valid wider angles remain supported. The source-backed numeric setting
`fIronSightsFOVTimeChange` supplies the transition duration and remains a live
consumer, including immediate zero-duration changes.

`FalloutWeaponSight` owns the flat angle and transition clock. The native camera
converts the source horizontal4:3 angle through `FalloutCameraProjection`; the
first-person render camera retains its independently configured projection.
OpenXR retains its runtime headset projection. Camera publication precedes shot
publication after the source weapon pose has advanced.

Shots read winning/executable auto-aim settings without a silent fallback and
use the separate third-person declaration. Candidate selection respects its
angular bound and rejects a nearer solid actor as an occluder. Telemetry retains
the camera origin/direction, corrected central direction, actual spread direction
and collision independently. Target ranking, screen-percentage behavior,
retail correction, scope overlays, transition blending and matched shot timing
remain unverified or unowned; these changes do not establish complete shooting.

Auto-aim query hulls now belong to the player lifetime rather than each shot.
Equipment preparation warms the first/third-person declarations; at most two
immutable hulls remain resident. Changed source range/angle declarations rebuild
their hull, invalid declarations do not replace one, and retirement disposes
both. The query transform, filters, angular tests, visibility and ranking remain
per shot. This removes repeated convex construction without altering target
selection or introducing a target whitelist.

Enemy target points now follow the player's source BPTD torso target on the
published world skeleton instead of a fixed locomotion height. Both flat poses
and tracked body displacement use that same anatomy. A presentation-free
locomotion fixture retains its capsule target; a bound body with a missing
source target fails visibly. The owned preparation fixture checks displaced
body targeting, camera independence and an actual ray into the damage volumes.

The shared weapon spread calculation selects either the single-arm or both-arm
injury setting. Previously it added both when both arms were crippled. This
correction follows the mutually exclusive injury states documented in the
[FNV spread investigation](https://geckwiki.com/index.php?title=Gun_Spread_Formula_%28New_Vegas%29).
It applies to players and NPCs. The existing sway-to-random-shot approximation,
NPC dispersion, intrinsic weapon dispersion and sight-to-impact alignment still
require direct comparison. These repairs do not establish bottle accuracy,
combat hit rates or physical headset acceptance.

The complete official-editor gate and owned weapon-preparation audit pass.
An optimized flat Vulkan session from an existing nearby combat checkpoint hits
the source creature twice at 2.69 and 1.93 meters, applies damage/death, completes
both ordinary F5 saves and quits with exit zero. The original checkpoint is
unchanged. Instrumented shot paths take 15.07 and 4.41 ms, excluding deferred
impact work and the rest of each frame. The first shot remains over the entire
144 Hz budget. This is a bounded combat/persistence check, not bottle-range
accuracy, enemy hit-rate, matched retail or headset acceptance.

Windows ExportRelease now enables .NET ReadyToRun compilation. The assembly
retains IL/runtime fallback; this is not NativeAOT or an engine replacement.
Other configurations and platforms retain their existing publishing behavior.
The same nearby combat scenario with a native-compiled export records 4.27 and
2.59 ms shot paths, completes both saves and quits zero. A separate cold process
resumes the retained reaction phase and returns to attack. Runtime content/AI
error categories match the ordinary export. These samples support moving cold
compilation out of gameplay, but neither the remaining frame work nor physical
XR performance has been accepted.

Synthetic contracts cover independent source flags, shorter admitted DNAM
layouts, angle/time selection, reversal, mutable settings, disabled/no-zoom
values, widening, projection conversion and invalid-state rejection. The
selected owned TTW BB gun audit compares its winning fields with the shared
owner and verifies release without changing source bytes. It is an isolated
source-clock fixture. Native shooting and matched retail evidence are separate.

Source documentation: [GECK Weapons](https://geckwiki.com/index.php/Weapons),
[auto aim settings](https://geckwiki.com/index.php/Auto_Aim_Settings) and
[game settings](https://geckwiki.com/index.php?title=Complete_List_of_GameSettings).
