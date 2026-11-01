using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutScriptLeveledItems
{
    internal static bool Supports(string name) => name == "additemtoleveledlist";

    internal static FalloutScriptFunction? Function(string name, FalloutPluginStack records,
        Func<FalloutLeveledItemState> owner) => !Supports(name) ? null : FalloutScriptFunction.Typed(
        [FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number,
            FalloutScriptArgumentKind.Number, FalloutScriptArgumentKind.Number], arguments =>
        {
            static int Integer(double value)
            {
                if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                    throw new InvalidDataException("Leveled entry argument must be an integer.");
                return (int)value;
            }
            owner().Add(arguments[0].Value.FormKey(records), arguments[1].Value.FormKey(records),
                Integer(arguments[2].Number), Integer(arguments[3].Number), (float)arguments[4].Number);
            return 0;
        });
}
