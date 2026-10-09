using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutStandaloneMainConstructor(Guid Identity, Guid Process, long Changed);
internal sealed record FalloutStandaloneMainPreludeCall(Guid Main, long MainOrdinal, Guid Constructor, Guid Process, long Changed);
internal sealed record FalloutStandaloneMainSnapshot(FalloutMainScriptCallerSource Source,
    FalloutMainScriptFrameSnapshot MainField, FalloutMainScriptCallerSnapshot MainCaller,
    FalloutMainPlayerCellSnapshot PlayerCell, FalloutStandaloneMainConstructor? Constructor,
    FalloutStandaloneMainConstructor? PreviousConstructor, FalloutStandaloneMainPreludeCall? LastPrelude);
