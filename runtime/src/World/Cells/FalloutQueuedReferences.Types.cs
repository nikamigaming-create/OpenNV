using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Source loader ownership is separate from native body publication and from
// the ProcessManager request list. A work consumer holds this exact object,
// never just a reference key which may already name a later queued object.
internal enum FalloutQueuedReferenceKind { Reference, Tree, Character, Creature, Player }
internal enum FalloutQueuedReferencePhase
{ Constructed, Reading, ReadReturned, Assembling, PublicationReturned, Cancelling, ConsumersRetired, MapRemoved, Destroyed }
internal enum FalloutQueuedReferenceConsumerKind { Read, Assembly, Publication, Cancellation }
internal sealed record FalloutQueuedReferenceSource(FalloutFormKey Reference, string ReferenceSignature,
    string ReferenceSha256, FalloutFormKey Base, string BaseSignature, string BaseSha256,
    FalloutQueuedReferenceKind Kind, bool EnginePlayer);
internal sealed record FalloutQueuedReferenceConsumer(Guid Identity, FalloutQueuedReferenceConsumerKind Kind,
    string Owner, Guid Process, long Entered, long Changed, bool Returned, string? Failure);
internal sealed record FalloutQueuedReferenceEntry(Guid Identity, FalloutQueuedReferenceSource Source,
    byte Priority, FalloutQueuedReferencePhase Phase, bool Mapped, bool CallerOwned, bool CancelRequested,
    IReadOnlyList<FalloutQueuedReferenceConsumer> Consumers, long Entered, long Changed,
    string Owner, string? Failure, string? Boundary, bool OpaqueOwnership);
internal sealed record FalloutQueuedReferenceMapEntry(FalloutFormKey Reference, Guid Value);
internal sealed record FalloutQueuedReferencesSnapshot(string Schema, string Stack, string Contract,
    Guid CapturedProcess, long Sequence, IReadOnlyList<FalloutQueuedReferenceEntry> Objects,
    IReadOnlyList<FalloutQueuedReferenceMapEntry> Map, FalloutActorProcessRuntimeHandoff? ColdHandoff);

// The runtime must supply the original factory's actual early inputs. The
// unrelated Main flag and projectile/Actor field are not derived from pause,
// a living Godot node or the C# AI-enabled Boolean.
internal sealed record FalloutQueuedReferenceFactoryInputs(FalloutActorProcessFact<bool> MainForced,
    FalloutActorProcessFact<bool> MainPermitsForcedQueue, FalloutActorProcessFact<bool> SourceBaseExcluded,
    FalloutActorProcessFact<bool> SourceReferenceHasQueueFlag, FalloutActorProcessFact<uint> SourceQueueFlags,
    string Owner);
internal enum FalloutQueuedReferenceRequestDisposition { SourceRefused, Existing, Constructed }
internal sealed record FalloutQueuedReferenceRequest(FalloutQueuedReferenceRequestDisposition Disposition,
    Guid? Identity, bool PriorityChanged, string Owner);
