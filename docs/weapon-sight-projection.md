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

Synthetic contracts cover independent source flags, shorter admitted DNAM
layouts, angle/time selection, reversal, mutable settings, disabled/no-zoom
values, widening, projection conversion and invalid-state rejection. The
selected owned TTW BB gun audit compares its winning fields with the shared
owner and verifies release without changing source bytes. It is an isolated
source-clock fixture. Native shooting and matched retail evidence are separate.

Source documentation: [GECK Weapons](https://geckwiki.com/index.php/Weapons),
[auto aim settings](https://geckwiki.com/index.php/Auto_Aim_Settings) and
[game settings](https://geckwiki.com/index.php?title=Complete_List_of_GameSettings).
