# Weather motion

Cloud motion reads the winning WTHR fifteen-byte DATA and four-byte ONAM
declarations. DATA's first byte normalizes wind to 0..1; each ONAM byte normalizes
its layer speed within the winning `fWeatherCloudSpeedMax` range. Their product
is UV displacement per simulation second. Calendar TimeScale does not multiply
this rate. Missing layouts or invalid maximum settings fail visibly.

The preceding renderer omitted weather wind and hard-coded the speed maximum.
For the inspected clear weather this made cloud motion 5.1 times too fast.
Synthetic checks exercise calm/zero layers, intermediate values, winning weather
and GMST overrides, and invalid declarations. The selected owned audit reads all
98 loaded weathers; it establishes source admission, not matched visual parity.

Run `FalloutPluginRuntimeProbe --audit-weather-motion <owned Data>`.
Fresh flat footage verifies advancing clouds. Transition blending, persistent
phase and matched retail/simulator timing remain open. Private executable
observation supports the multiplicative motion contract; the installed weather
getter is hooked, so this is not a claim of complete unmodified-retail parity.

Rigid-body wind is separate from cloud texture motion. The owned tumbleweed
declares wind response in its NIF body flags. That flag currently has no force
owner, and gravity/initial rolling alone does not establish sustained wind or
correct reentry behavior.
