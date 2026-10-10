using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledRecurringEventFilter(ushort Event, uint? MenuCode)
{
    internal static FalloutCompiledRecurringEventFilter Read(FalloutCompiledEvent block)
    {
        if (block.Event is not (0 or 1))
            throw new NotSupportedException($"Compiled quest event {block.Event:x4} has no recurring dispatch owner.");
        if (block.Parameters.IsEmpty) return new(block.Event, null);
        var operand = new FalloutCompiledOperandCursor(block.Parameters);
        var count = operand.UInt16();
        if (count == 0) { operand.RequireEnd(); return new(block.Event, null); }
        if (block.Event != 1 || count != 1)
            throw new NotSupportedException("Compiled recurring event argument count/type has no original filter owner.");
        if (operand.Byte() != 'n')
            throw new NotSupportedException("Compiled MenuMode variable/query filter requires its actual operand/event-list owner.");
        var code = BinaryPrimitives.ReadUInt32LittleEndian(operand.Bytes(4).Span);
        operand.RequireEnd();
        return new(block.Event, code);
    }

    internal bool Matches(FalloutScriptMenus menus) => Event == 0 ? menus.GameMode :
        !menus.GameMode && menus.Query(MenuCode is { } code ? code : null) != 0;

    internal static IReadOnlyList<int> Select(FalloutCompiledScriptProgram program, FalloutScriptMenus menus) =>
        Enumerable.Range(0, program.Events.Count).Where(ordinal => Read(program.Events[ordinal]).Matches(menus)).ToArray();

    internal static IReadOnlyList<int> Select(FalloutCompiledScriptProgram program, bool gameMode,
        IEnumerable<uint>? codes)
    {
        var menus = new FalloutScriptMenus(); menus.Publish(gameMode, codes);
        return Select(program, menus);
    }
}
