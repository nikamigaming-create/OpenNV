using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerModelStartupIngress { CharacterGenerationStart, LoadingMenuTeardown, UnownedWriter }
internal enum FalloutPlayerModelStartupStep { Entered, SelectedQuest, ByteStored, Invalidated, Returned }
internal sealed record FalloutPlayerModelStartupInvocation(Guid Call, Guid Process, int ThreadId,
    FalloutPlayerModelStartupIngress Ingress, FalloutFormKey? Quest = null);
internal sealed record FalloutPlayerModelStartupPublication(Guid Process, Guid Factory, Guid Constructor,
    Guid RecordsFactory, long Published, byte Value, Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPlayerModelStartupCall(FalloutPlayerModelStartupInvocation Invocation, Guid Factory,
    Guid Constructor, long Entered, long Changed, FalloutPlayerModelStartupStep Step, byte Before, byte After,
    long? Stored, string? Setting, FalloutFormKey? SelectedQuest, string? WriterBoundary, bool Returned,
    string? Failure, bool EnclosingHelperReturned = false);
internal sealed record FalloutPlayerModelStartupRead(FalloutReferenceModelDataInvocation Invocation, string Source,
    string Stack, Guid Process, Guid Factory, Guid Constructor, Guid RecordsFactory, long Sequence, byte Value);
internal sealed record FalloutPlayerModelLoadGateRead(FalloutReferenceModelDataInvocation Invocation, string Source,
    Guid Factory, FalloutPlayerModelChangeFlagsRead Flags, FalloutPlayerModelStartupRead? CharacterGeneration,
    bool InventoryPreparationRequired);
internal sealed record FalloutPlayerModelStartupSnapshot(string Schema, FalloutPlayerModelLoadPrerequisiteSource Source,
    string Stack, string RecordsSource, Guid Process, Guid Factory, Guid Constructor, long Sequence, byte Value,
    IReadOnlyList<FalloutPlayerModelStartupPublication> Publications, IReadOnlyList<FalloutPlayerModelStartupCall> Calls,
    string? WriterBoundary, string? Failure, bool Retired);
