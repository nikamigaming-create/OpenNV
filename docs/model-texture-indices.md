# Model alternate texture indices

The model texture-swap owner keys alternate entries by their source 3D index.
It enumerates geometry in depth-first scene-child order, rather than NIF block
table order. Particle geometry consumes an index; null children and lights do
not. Unreferenced blocks cannot become indexed targets. Hidden geometry retains
its index. Later entries for the same index replace the selected texture form.

Stored shape names remain source labels. The observed native apply path does not
compare them or use them to redirect an index. Owned models can legitimately
contain repeated names and record labels that differ from the indexed shape.
OpenNV retains those declarations while applying the indexed source texture set.
It rejects out-of-range indices, cyclic/shared scene-child traversal and unknown
scene objects. Particle texture substitution remains visibly unbound.

The immutable geometry order is reused within each decoded NIF's lifetime.
Actor and equipped weapon material binding share it. The native synthetic audit
(`NativeNifInstanceAudit --model-textures`) covers reordered/nested children,
repeated and stale names, duplicate-index replacement, null children,
unreferenced geometry, particle index consumption and unsupported-owner refusal.
Both affected owned Vault security actors pass complete mounted-stack assembly
with their source inventory and materials. These checks do not establish matched
retail pixels or birthday/campaign completion.
