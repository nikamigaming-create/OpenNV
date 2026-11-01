using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.World.Cells;

// Native IDLE used-item queries inspect a shared selection context. Possession
// and the condition's actor subject do not establish that context.
internal sealed partial class FalloutReferenceWorld
{
    private sealed record UsedItemContext(FalloutFormKey Item, int? Level, bool? Activate);
    private UsedItemContext? _usedItemContext;

    internal IDisposable EnterUsedItem(FalloutFormKey item, int? level = null, bool? activate = null)
    {
        _ = records.GetEffective(item);
        var previous = _usedItemContext;
        var current = new UsedItemContext(item, level, activate);
        _usedItemContext = current;
        return new UsedItemLease(this, current, previous);
    }

    private sealed class UsedItemLease(FalloutReferenceWorld world, UsedItemContext current, UsedItemContext? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (!ReferenceEquals(world._usedItemContext, current)) throw new InvalidOperationException("Used-item contexts retired out of order.");
            world._usedItemContext = previous;
            _disposed = true;
        }
    }

    internal float QueryUsedItem(FalloutConditionQuery condition)
    {
        var context = _usedItemContext;
        switch (condition.Kind)
        {
            case FalloutConditionFunction.GetUsedItemLevel:
                return context is null ? -1 : context.Level ?? throw new NotSupportedException("Used-item selection level has no height owner.");
            case FalloutConditionFunction.GetUsedItemActivate:
                return context is null ? 0 : context.Activate is { } activate ? activate ? 1 : 0 :
                    throw new NotSupportedException("Used-item selection has no activation owner.");
            case FalloutConditionFunction.GetIsUsedItem:
                if (context is null || condition.IsNullFormArgument1(records)) return 0;
                var form = condition.FormArgument1;
                return (records.GetEffective(form).Signature == "FLST" ? FormLists.Entries(form).Contains(context.Item) : form == context.Item) ? 1 : 0;
            case FalloutConditionFunction.GetIsUsedItemType:
                return context is not null && condition.Argument1 != 0 &&
                    UsedItemType(records.GetEffective(context.Item).Signature) == condition.Argument1 ? 1 : 0;
            case FalloutConditionFunction.IsWaterObject:
                if (context is null) return 0;
                var source = records.GetEffective(context.Item);
                if (source.Signature == "PWAT") throw new NotSupportedException("Used placeable water requires its water-form link owner.");
                if (source.Signature != "ACTI") return 0;
                var fields = source.ReadSubrecords().Where(field => field.Signature == "WNAM").ToArray();
                if (fields.Length == 0) return 0;
                if (fields.Length != 1 || fields[0].Data.Length != 4) throw new InvalidDataException("Water activator has an invalid WNAM.");
                var water = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
                if (water is null) return 0;
                if (records.GetEffective(water.Value).Signature != "WATR") throw new InvalidDataException("Activator water source is not WATR.");
                return 1;
            default: throw new InvalidDataException("Condition is not a used-item selection query.");
        }
    }

    private static uint UsedItemType(string signature) => signature switch
    {
        "ACTI" => 21,
        "TACT" => 22,
        "TERM" => 23,
        "ARMO" => 24,
        "BOOK" => 25,
        "CONT" => 27,
        "DOOR" => 28,
        "INGR" => 29,
        "LIGH" => 30,
        "MISC" => 31,
        "STAT" => 32,
        "SCOL" => 33,
        "MSTT" => 34,
        "PWAT" => 35,
        "TREE" => 37,
        "FLOR" => 38,
        "FURN" => 39,
        "WEAP" => 40,
        "AMMO" => 41,
        "NPC_" => 42,
        "CREA" => 43,
        "KEYM" => 46,
        "ALCH" => 47,
        "IDLM" => 48,
        "NOTE" => 49,
        _ => throw new NotSupportedException($"Used-item type for {signature} has no qualified native identity.")
    };
}
