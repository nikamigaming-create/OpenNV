using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutStandaloneMenuFieldStep
{
    Constructor, Root, Identity, Push, Visible, Transition, Remove, InputClose, QuietReturn, Returned
}
internal enum FalloutStandaloneMenuFieldOperation { BindRoot, StoreVisible, ReplaceTransition, AdvanceTransition, ForceRemove, NormalRemove, QuietReturn }
internal sealed record FalloutStandaloneMenuFields(Guid Object, Guid Process, string Source,
    uint ClassCode, uint Identity, uint Lifecycle, ushort Flags, long Constructed, long Changed,
    FalloutUiComponentBinding? Root, uint? Visible, long? Closed, long? Retired,
    uint? RegisteredCode = null, uint? CloseReferenceBits = null);
internal sealed record FalloutStandaloneMenuFieldWrite(Guid Invocation, Guid Object, Guid Process, string Source,
    FalloutStandaloneMenuFieldOperation Operation, long Entered, long Changed,
    FalloutStandaloneMenuFieldStep Step, IReadOnlyList<uint> BeforeSlots, IReadOnlyList<uint> AfterSlots,
    uint BeforeMode, uint AfterMode, uint Context, uint? Visible, int? Result,
    byte? ImageMenuByte, bool ImageReturned, bool Returned, string? Failure,
    FalloutSourceThreadKindScope? Allocation = null, uint? Duration = null,
    FalloutOriginalMenuClose? InputClose = null, FalloutOriginalMouseBufferFlush? MouseFlush = null,
    FalloutPlayerControlHudFrame? Frame = null, uint? Progress = null,
    IReadOnlyList<FalloutSourceMenuImageWrite>? ImageWrites = null, uint? CloseReferenceBits = null, FalloutStandaloneMenuFields? BeforeFields = null, FalloutStandaloneMenuFields? AfterFields = null);
internal sealed record FalloutStandaloneMenuFieldsSnapshot(string Source, Guid Process, long Sequence,
    IReadOnlyList<FalloutStandaloneMenuFields> Menus, IReadOnlyList<FalloutStandaloneMenuFieldWrite> Writes,
    IReadOnlyList<FalloutStandaloneInterfaceColdHandoff> ColdConstructors,
    IReadOnlyList<FalloutStandaloneMenuInputLifetime> Inputs);
internal sealed record FalloutStandaloneMenuInputLifetime(Guid Process, FalloutOriginalInputSnapshot State);

internal sealed class FalloutStandaloneMenuFieldInvocation
{
    internal FalloutStandaloneInterfaceState Owner { get; }
    internal Guid Identity { get; }
    internal Guid Object { get; }
    internal Guid Process => Owner.Process;
    internal string Stack => Owner.Stack;
    internal FalloutStandaloneMenuFieldStep Step { get; set; }
    internal FalloutStandaloneMenuFieldInvocation(FalloutStandaloneInterfaceState owner, Guid identity, Guid menu)
    { Owner = owner; Identity = identity; Object = menu; }
    internal void Require(FalloutStandaloneMenuFieldStep step) => Owner.RequireMenuFieldChild(this, step);
}
