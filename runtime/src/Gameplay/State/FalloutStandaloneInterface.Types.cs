using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutStandaloneInterfaceObjectRole { PipBoyManager, DialogMenu, OtherMenu }
internal enum FalloutStandaloneInterfaceQuery { MenuGate, GuiModeTwo, FirstPredicate, FinalPredicate, ForeignMenu, ContextKind }
internal sealed record FalloutStandaloneInterfaceNativePublication(Guid Factory, Guid Process,
    string Source, string Stack, FalloutStandaloneInterfaceObjectRole Role, ulong NativeInstance,
    string Resource, string ResourceSha256, string Owner);
internal sealed record FalloutStandaloneInterfaceObject(Guid Identity, Guid Process,
    FalloutStandaloneInterfaceObjectRole Role, long Constructed, long? Closed, long? Retired,
    FalloutStandaloneInterfaceNativePublication Native);
internal sealed record FalloutStandaloneInterfaceColdHandoff(Guid PreviousProcess, Guid CurrentProcess,
    Guid PreviousConstructor, Guid CurrentConstructor, long Changed);
internal sealed record FalloutStandalonePipBoyManagerFields(int Selection, byte FirstFlag, byte SecondFlag,
    byte ThirdFlag, ushort Word, byte FinalFlag, byte BaseFlag, uint FinalWord,
    IReadOnlyList<Guid?> References, IReadOnlyList<uint> Words)
{
    internal static FalloutStandalonePipBoyManagerFields Constructed => new(-1, 0, 0, 0, 0, 0, 1, 0, new Guid?[13], new uint[9]);
}
internal sealed record FalloutStandaloneInterfaceSnapshot(string Schema, FalloutStandaloneInterfaceSource Source,
    string Stack, Guid CapturedProcess, Guid Constructor, long Sequence, byte Enabled,
    uint Mode, uint Context, IReadOnlyList<uint> MenuSlots, IReadOnlyList<byte> ActiveMenus,
    byte DialogByte, FalloutConsolePresence Console, sbyte? ConsoleCounter,
    FalloutStandaloneInterfaceObject? OwnManager, FalloutStandalonePipBoyManagerFields? ManagerFields,
    FalloutStandaloneInterfaceObject? Dialog, IReadOnlyList<FalloutStandaloneInterfaceObject> Objects,
    Guid? Alternate, FalloutStandaloneInterfaceColdHandoff? ColdHandoff, string? Boundary,
    string? Failure, bool Retired, FalloutStandaloneMenusSnapshot Menus);
