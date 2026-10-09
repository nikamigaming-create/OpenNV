using System.Collections.Immutable;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed class NativeNvseEngineCommandBinding(uint id, ulong generation, ulong module,
    uint thread, FalloutEngineCommandBooleanLeaf source)
{
    internal uint Id { get; } = id;
    internal ulong Generation { get; } = generation;
    internal ulong Module { get; } = module;
    internal uint Thread { get; } = thread;
    internal FalloutEngineCommandBooleanLeaf Source { get; } = source;
    internal ulong CallableLease { get; set; }
    internal uint NativeTarget { get; set; }
    internal bool NativePublished { get; set; }
    internal bool NativeRetired { get; set; }
    internal bool ClosedByProcessExit { get; set; }
    internal bool Failed { get; set; }
    internal HashSet<uint> SourceDescriptors { get; } = [];
    internal List<NativeNvseCommand> Registrations { get; } = [];
    internal bool Live => NativePublished && !NativeRetired && !ClosedByProcessExit && !Failed;
}
internal sealed record NativeNvseEngineCommandEvent(ulong Generation, ulong Module, uint Thread,
    ulong Callback, ulong Parent, NativeNvseHostCall Event, uint Binding, uint SourceAddress,
    string RuntimeSha256, string BodySha256, bool? BooleanResult, ImmutableArray<uint> Arguments,
    ulong CallableLease, uint NativeTarget, string? Failure);
