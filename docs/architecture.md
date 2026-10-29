# OpenNV architecture

OpenNV is a first-party C# and Godot/OpenXR game implementation. C# owns binary
readers, winning records, gameplay, persistence and diagnostics. Godot owns
presentation, controller/input adaptation and OpenXR. Flat and VR share the
same gameplay and saves. Owned retail files are read-only; no retail assets or
derivatives are packaged. There is no OpenMW dependency.

## Product path

RuntimeCoordinator and its launcher select an installation, profile and mode.
RuntimeLiveContentSource reads installed resources directly. Official-content
selection excludes custom activation lists and native extensions. Mod profiles
remain a later explicit capability. Loose resources physically installed in the
selected directory still follow resource precedence; official ESM selection does
not assert that an installation is pristine.

RuntimeCoordinator.NativeOwned constructs source-authored cells and references.
RuntimeNativeQuestScripts advances the authoritative script runtime.
RuntimeNativeOpeningStageDriver connects quest, conversation, package, combat,
inventory and world owners. RuntimeNativePlayer owns locomotion and actions.
Existing door and exterior-streaming owners load connected spaces. Persistent
campaign snapshots retain gameplay state for both flat and OpenXR continuation.

FalloutConditionRuntime shares query semantics across actor packages, dialogue,
idle, quest, menu, terminal, unloaded-reference and parsed-script consumers.
FalloutConditionScope resolves invocation subjects; FalloutConditionQuery keeps
script-bound forms distinct from raw declaring-plugin FormIDs. Capabilities
query existing state owners, with physical/native process predicates delegated
to their actual actor or player adapters. Retiring an owner removes its callback;
an absent owner never supplies a false/zero result. Ordered CTDA evaluation owns
comparisons and lazy OR, including source-global comparisons. SCDA instruction
decoding/execution remains a separate missing VM layer; SCTX execution is not
evidence of compiled-bytecode support.

Content.Definitions owns source condition-function and actor-value identifiers,
canonical names and aliases, with neutral metadata references. It depends on
neither Godot nor mutable gameplay owners. Readers preserve raw source fields;
runtime dispatch consumes typed identifiers; state owners decide which values
and formulas they can provide; Godot/native adapters publish actual process
state. SourceSlot/SourceIndex/UserSlot, limb lookup and script query binding use
the same cached identifier/name definition. Recognizing an identifier does not
create a state pool or authorize a default result.

FalloutFormList keeps winning source declarations immutable. The world-owned
FalloutFormListState supplies ordered mutable additions and atomic validated
restoration to every live list consumer. FalloutActorAiData caches immutable
selected-template metadata; actor/player pools retain independent modifiers and
provenance. FalloutActorValueCommands owns shared argument and pool semantics;
script adapters route health/limb restoration to their existing specialized owners.
These state owners do not depend on presentation or create dynamic references.
FalloutEngineIdentity binds named reserved identities into the stack namespace;
recognizing PlayerRef does not require a placed source record or create its base
state. Base/actor-value queries still require their actual source and state owners.

NativeXrRig adapts tracked head/controller poses and ordinary action input to the
same player. RuntimeNativePlayerActor owns body, hands and weapon presentation.
NativeOwnedPipBoy renders the shared menu on the equipped wrist device.
NativeXrCanvas renders ordinary menus on a stereo surface. Input ownership must
release when a menu closes, tracking disappears or a session retires.

## Implementation discipline

Keep one owner per state transition and avoid parallel gameplay paths. Decode
and reuse resources in process; transformed retail assets are not launch inputs.
Immutable declaration indexes live with the plugin stack; fresh gameplay owners
reuse them while retaining independent activation clocks and radio discovery.
Source preparation is cancellable and never publishes partial indexes.
Do not introduce named-location exceptions, proxy actors or forced quest success.
Implement missing behavior at its real owner and keep unsupported behavior visible.
Use bounded, relevant checks and ordinary gameplay to assess the result. Record
counts and plausible images do not establish campaign completion or parity.

Original classic glTF meshes keep their linear vertex colors. Plain opaque
double-sided vertex-painted surfaces use a PBR render-input adapter for
Compatibility; source-palette and textured materials retain their existing owners.
Starting a player reuses a preview only when its source MAP hash, path and
elevation match, avoiding a duplicate world.

The incomplete mandatory source-Main/Player model was removed during the October
17 recovery. Its prior work remains in Git history. New mechanics must integrate
with the playable implementation rather than recreate a compulsory unfinished
scheduler. Implementation details may be replaced freely when source behavior
and practical validation justify the change.
