# Managed object morphs and particle channels

Managed NiGeomMorpherController channels bind the declared geometry, controller
and named target. Relative NiMorphData supplies the base vertices at weight one
and additive target vectors; source scalars publish into the existing native
blend-shape mesh. Source normals/tangents retain the existing packed basis.
Skinned managed morphs, additional controller inputs and unknown targets remain
unbound. Every animated instance builds its own mesh/controller owners from the
in-process decoded source. The existing object animation clock retains source
selection and cold phase.

NiPSysEmitterLifeSpanCtlr shares the existing source scalar and particle owner.
The [NIF declaration](https://www.niftools.org/nifxml/NiPSysEmitterLifeSpanCtlr.html)
identifies emitter lifespan as its target. A sampled lifespan applies to new
births; live particles keep the lifetime assigned at birth. Instance reset
restores the source emitter values. Invalid modifier names, negative sampled
lifespans and unknown controller fields remain visible failures.

NiPSysColliderManager and linked NiPSysPlanarCollider records retain their
source modifier order, manager, object basis, dimensions, bounce/death flags and
next link. See the [public NIF declarations](https://raw.githubusercontent.com/niftools/nifxml/develop/nif.xml).
The runtime checks source ownership, cycles and orthonormal plane axes, then
sweeps point trajectories against bounded front-facing rectangles. Contact
consumes its motion time before the position modifier advances the remaining
interval; bounce retains tangential velocity. Secondary collision spawning and
other collider shapes remain unbound. Exact retail sidedness, moving-plane
response, particle identity/randomness and contact ordering remain unaccepted;
telemetry exposes those boundaries. This is not a retail collision parity claim.

Synthetic native checks cover source base/relative geometry, scalar publication,
instance independence, cold phase, bad binding rejection, new-birth lifespan,
linked plane contacts, invalid chains/owners and source reset. The selected owned
creature audit now loads the complete cake model, executes its actual PlayGroup
Forward result, runs the source cutting idle, retains that idle through actor
reassembly, and executes the source package change result once. The audit uses
an isolated floor and explicit stage/enable-parent fixture. It does not execute
the complete stage program or establish campaign cold continuation or matched
retail pixels. The ordinary closed80 Continue, source door/escort, Amata replies
and Palmer approach reach the real cake event. Native Travel, PlayGroup Forward,
the complete cutting idle and its package change result execute, entering16.
The following authored Andy SayTo player GREETING exposes the separate scripted
topic/quest selection owner. That fault and its consumed prefix remain retained.
The required Release/Debug, formatting/analyzer, contract, launcher and native
Godot gate passes. Birthday completion and the subsequent campaign remain open.
