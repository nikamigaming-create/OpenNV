# Pip-Boy device motion

The rendered-menu device has separate owners for first-person arm animation,
the tab-selector knob, the scroll knob, the radiation needle, indicator glows,
and screen effects. Page identity or a changing screen does not prove motion.

## Tab-selector contract

The owned model's `TabKnob` node is the pivot. The rendered-menu owner replaces
its local rotation, preserving its source translation and scale. Rotation is
clockwise about native Z (Godot Y); it is not an extra rotation multiplied into
the authored rest rotation. Its child geometry follows the original hierarchy.

There are five evenly spaced subtab stops, including both endpoints from the
owned `[Pipboy]` settings `fTabKnobMinPosition` and `fTabKnobMaxPosition`.
Subtab index, rather than the Stats/Items/Data page index, selects the stop.
The current angle pursues the target at `fTabKnobMoveRate` radians per menu-clock
millisecond, capped at the target in either direction. Changing targets during
motion retains the current angle. The owner advances while gameplay is paused.
An invalid clock, tab, node binding or setting remains an explicit error.

These rules were qualified from the read-only owned model and a bounded private
Win32 observe-mode inspection of the running retail manager and its update
operations. Public code reads settings from the user's installation; observed
numeric defaults and private process addresses are not runtime inputs.

## Hand action and radiation needle

The first-person source Pip-Boy sequence advances to its `Hit` text key and
holds while the menu is active. Closing resumes that same sequence forward to
its authored stop; it does not play the opening prefix backward. A bounded
ordinary close/reopen observation retained the held source time, monotonic
advance through the release interval, retirement, and the next opening's
advance back to `Hit`. The implementation now samples the full source interval.
The separate raise/lower transition blend remains unqualified.

The `RadNeedle` pivot rotates clockwise about native Z. Its initial angle is
`pi/2 - pi * max(0, playerRadiationRads) / 1000`. Subsequent targets add a centered
uniform perturbation whose full width is `1000 * StatsMenu.user4` rads. The
StatsMenu's source `user3` supplies the update interval in seconds. At each
deadline it draws one target and sets the next deadline from the current menu
clock; delayed frames do not replay missed targets. The needle pursues targets
at 0.0005 radians per millisecond, including while gameplay is paused. The source
XML is decoded once per owned content source. Cosmetic random draws use an
independent presentation stream; native random-stream correspondence remains
unverified and does not consume authoritative gameplay randomness.

## Remaining lanes

The source first-person Pip-Boy clip and waver drive the hands. Their raise/lower
blending, exact opening/closing event timing, and interaction-specific hand
publication still need matched evidence. Scroll-knob event binding, physical
button motion, CRT effects, and their audio/timing remain unqualified. A button's page
selection or indicator glow is not evidence of a physical press animation.

The tab owner publishes current/target angles in the existing device telemetry
and restores the borrowed persistent XR node on retirement. Flat and physical
XR final-pixel and timing acceptance remain open.

## Menu illumination lifetime

Flat mode owns disposable menu arms and materials. XR borrows the persistent
equipped wrist device. A dormant XR view restores its source screen material,
indicator/light-effect visibility, surface overrides and instance environment.
Focusing uses separate cloned material parameters for menu pixels and lighting;
unfocus, retirement and preparation failure restore the borrowed world state.
Each focus edge reacquires that state so an intentional change made while the
device is dormant survives the next close. The existing telemetry reports whether
the menu owns this borrowed material state.

The flat rendered-menu light is created at the source `Omni01` anchor (with the
engine's `Omni01:0` fallback), using the owned `[InterfaceFX] fScreenLightRadius`
and Pip-Boy system color. It illuminates the menu arms/device independently of
the held world flashlight. The device NIF has an anchor rather than an authored
`NiPointLight`; enumerating only NIF lights silently omitted this owner. CRT
pulse contributions and focused XR screen lighting remain unqualified.

Flat first-person and Pip-Boy isolated viewports now receive the live source
world's ambient, directional and overlapping point-light inputs, including the
authoritative world flashlight when enabled. A shared presentation node caches
live light owners through scene add/remove signals, filters actual World3D,
visibility and cull masks, and maps world positions through the current world
camera into isolated camera space with explicit source/view unit conversion.
Godot's sRGB light property is decoded back to the source numeric shader color
before custom NIF binding. Source ranges and current light energy/flicker survive;
there is no brightness tuning or nearest-N truncation. Lights beyond the original
eight uniform slots use floating-point data textures. Bridged materials own the
complete diffuse input and suppress a second hardware-light contribution from
isolated presentation copies. Actual target-model local lights, including the
first-person muzzle, enter that binding in target-camera coordinates and units.
Skinned actor parts use the existing NiSkinData bone spheres transformed by the
current shared pose rather than the unchanged ArrayMesh bind-pose AABB. Skinned
attachments without that source bound retain lights conservatively and publish
their count; attenuation still uses the source light radius. Native light selection, shadow visibility, specular
response and matched final pixels remain unverified.

The screen now has a separate interface-effect owner shared by flat and XR.
Its source parameters are `fBlurRadiusPipboy`, `fBlurIntensityPipboy` and
`fScanlineFrequencyPipboy` from `[Pipboy]`, plus the registered `[InterfaceFX]`
pulse, burst and disturbance settings. The legacy `fScanlineScalePipboy` and
`fBrightenPipboy` names in an INI do not establish active bindings. No blanket
brightness multiplier is applied.

Menu, scanline and disturbance samples retain their encoded source numbers
through the additive/multiplicative composition. The finished color crosses one
encoded-to-linear transfer at spatial ALBEDO, matching the existing isolated
NIF output contract. Converting each texture before composition would change
the native arithmetic; writing the finished encoded color directly to ALBEDO
would apply the Godot output transfer a second time. The disturbance factor
0.35 is a literal in the owned interface shader, not a visual adjustment.

The source composition adds an equal-weight nine-sample neighborhood, with
offsets of blur radius divided by source texture width and height, modulated by
blur intensity and system color. The authored scanline texture multiplies the
result at the source frequency; the owned default is selected through the
registered `SScanlineTexture` string setting. The separate owned
`PipboyDistortEffectMap.dds` supplies a traveling additive band. Its vertical
coordinate is one plus screen Y times vertical scale minus elapsed/duration;
the horizontal coordinate is one half. Its contribution is system-color tinted
and multiplied by horizontal scale and 0.35. This pass does not apply a barrel
warp: the owned screen geometry remains responsible for curvature.

Opening applies the source linear burst decay over `fDefaultBurstDuration`.
Pulse modulation uses independently generated gradient/permutation tables and
the source one-dimensional quintic gradient-noise interpolation, doubled and
scaled by the radius/brightening pulse settings. It is not a sine oscillation.
The tables, absolute clock phase and interval RNG do not establish the retail
random stream. The traveling band's duration field retains source integer
truncation; after progress exceeds vertical scale plus one it waits for the
source random interval domain before restarting. Effects continue with the
presentation clock while gameplay is paused.

OpenNV currently supplies its authoritative menu texture for both mask and color
terms. Separate native render-target composition, page-event burst dispatch,
vertical-hold/shudder activation, transition visibility/fade and matched pixels
remain unbound. Local source text alpha stays with the menu drawing owner; this
screen pass does not infer glyph opacity by multiplying invisible hit surfaces.
The existing lighting-cache material audit compiles the interface shader. This
does not replace a packaged matched screen check.

The flat ownership audit found fresh `ShaderMaterial` instances for arm/device
construction. Shared caches contain shader programs, textures and prepared
managed geometry arrays rather than mutable material parameters. Menu bindings
write per-material parameters and per-mesh instance environment, and do not
publish global exterior environment. No flat-menu-to-room material alias leak
was identified in these owners; this does not establish correct room lighting.

The world flashlight is separate: the native light toggle applies/removes its
source light spell and updates first/third-person light-effect presentation.
The world owner resolves that spell's winning effect/light graph, magnitude,
source radius conversion and actor-bound offsets. A short Menu press opens the
device on release; crossing the source hold threshold toggles the light once
and suppresses menu opening on release. Persistent on/off state is independent
of transient held input and validates the source graph on restore. Light
publication runs even when weapon presentation exits early, so an off request
cannot leave a previous visible light frozen on.

The world owner is shared by flat and XR. Physical XR controller binding remains
unimplemented; existing combat-wheel controls are preserved. Flat hands and
weapons use a separate viewport whose world-light bridge remains incomplete.
Room brightness still differs in the current paired view. Restoring menu
materials and implementing the flashlight do not establish room lighting parity.
