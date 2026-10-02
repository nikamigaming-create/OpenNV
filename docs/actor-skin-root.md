# Actor skin root and influence binding

A skin instance's root identifies the model coordinate frame. Its display name
does not have to occur in the receiving actor skeleton. Only the referenced
influence palette is rebound by bone name. The
[NIF schema](https://github.com/niftools/nifxml/blob/develop/nif.xml)
keeps the armature root pointer separate from its bone influence list.

The native actor-part builder admits a top-level source skin root or a nested
source root named for a top-level receiving actor bone. It retains the
authored geometry and skin transforms and binds each influence to the actor's
actual named bone. Missing influence names still fail. It does not create a new
bone, use a rigid attachment, rename the source or discard its surfaces.

`NativeNifInstanceAudit --actor-skin-root` exercises independent model/actor root
names, a nested root bound to the external actor root, an animated influence
and rejection of a missing influence. The
`--owned-actor-skins` mode assembles specified owned models with their source
materials and verifies nonempty surfaces and valid native skin bindings.
These checks do not establish final-pixel or matched-retail parity.
