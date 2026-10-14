using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutInitialPlacementCellLane { CurrentCell, Grid }
internal enum FalloutInitialPlacementCellStep { Entered, Setting, Diameter, Slots, CurrentCell, GridSlot, Selected, UnownedWriter, Returned }
internal sealed record FalloutInitialPlacementCellStore(FalloutInitialPlacementCellStep Step, long Changed,
    uint? Bits = null, int? Index = null, FalloutFormKey? Cell = null);
internal sealed record FalloutInitialPlacementCellRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, long Sequence, FalloutFormKey? CurrentCell, uint? Diameter, int? Slot, FalloutFormKey? Cell);
internal sealed record FalloutInitialPlacementCellCall(FalloutReferencePositionInvocation Invocation, string Source,
    Guid Process, Guid Factory, long Entered, long Changed, string Kind, FalloutInitialPlacementCellStep Step,
    IReadOnlyList<FalloutInitialPlacementCellStore> Stores, bool Returned, string? Failure,
    FalloutIniValue? Setting = null, bool SettingStored = false, FalloutInitialPlacementCellRead? Read = null,
    FalloutInitialPlacementCellLane? Invalidated = null, string? Writer = null);
internal sealed record FalloutInitialPlacementCellSnapshot(string Schema, FalloutInitialPlacementCellsSource Source,
    string Stack, Guid Process, Guid Factory, Guid Constructor, long Sequence, FalloutFormKey? CurrentCell,
    uint Diameter, IReadOnlyList<FalloutFormKey?> Slots, IReadOnlyList<FalloutReferencePositionPublication> Publications,
    IReadOnlyList<FalloutInitialPlacementCellCall> Calls, IReadOnlyDictionary<FalloutInitialPlacementCellLane, string> Writers, bool Retired);
