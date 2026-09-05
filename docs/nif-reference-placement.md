# Placed NIF transform ownership

A placed reference supplies the loaded model root's complete transform. The
exported NIF root transform is replaced, while descendant local transforms are
preserved. Composing both transforms can rotate an entire architecture module
inside an otherwise correctly positioned cell.

`RuntimeNativeNifPrototype.InstantiatePlaced` implements this ownership for the
ordinary placed-object path. It records the authored root transform in diagnostic
metadata, resets that model root, and applies the source reference placement to
the instance. Standalone models and menu devices retain their authored roots.
Multiple source roots remain unsupported until their placement contract is
established. Actor assembly has a separate ownership path.

The synthetic native instance audit uses a translated, rotated, scaled source
root to distinguish replacement from composition and checks standalone behavior.
The selected `NativeNifInstanceAudit --placement` audit reads owned cell/model
data and a private native observation. On 2026-09-05, 19 root and descendant
transforms across three architecture references agreed after coordinate
conversion, using approximate float comparison. A normal opening replay visibly
removed the obstructing quarter-turned wall and restored the view through the
room. This is a component result; exact transforms, collision behavior, complete
cell geometry, animated root ownership and final-pixel parity remain unverified.

The same investigation exposed an inspector coverage defect: a box crossing the
camera plane was discarded even when its walls were visible. Render tracing now
clips box edges against the near plane before projection. Its native audit
exercises a box surrounding the camera, fully clipped geometry and a rotated
placement in perspective and orthographic views. These are static bounds used
for candidate selection; they do not identify exact pixel contributors.

Native observations, captures and owned model payloads stay private and are not
repository inputs.
