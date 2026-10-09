using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptLocalMetadata(int Ordinal, uint Index, byte StorageFlags, string Name);

internal static partial class FalloutScriptLocals
{
    private sealed record LocalLayout(IReadOnlyList<FalloutScriptLocalMetadata> Entries,
        IReadOnlySet<uint> MixedSlots, IReadOnlySet<uint> ReferenceSlots);
    private static readonly ConditionalWeakTable<FalloutPluginRecord, LocalLayout> Layouts = new();

    // Preserve every declared entry and its order. Named aliases do not create
    // another index, and a later entry never renumbers the first matching cell.
    internal static IReadOnlyList<FalloutScriptLocalMetadata> ReadMetadata(FalloutPluginRecord script) => Layout(script).Entries;
    internal static bool HasMixedStorage(FalloutPluginRecord script, uint slot) => Layout(script).MixedSlots.Contains(slot);
    internal static bool HasReferenceView(FalloutPluginRecord script, uint slot) => Layout(script).ReferenceSlots.Contains(slot);

    private static LocalLayout Layout(FalloutPluginRecord script) => Layouts.GetValue(script, source =>
    {
        if (source.Signature != "SCPT") throw new InvalidDataException("Variable metadata owner is not SCPT.");
        var entries = new List<FalloutScriptLocalMetadata>();
        var references = new HashSet<uint>();
        (uint Index, byte Flags)? pending = null;
        foreach (var field in source.ReadSubrecords())
        {
            if (field.Signature == "SLSD")
            {
                if (field.Data.Length != 24 || pending is not null)
                    throw new InvalidDataException("Script variable declaration extent or name is invalid.");
                pending = (BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span), field.Data.Span[16]);
            }
            else if (field.Signature == "SCVR")
            {
                var entry = pending ?? throw new InvalidDataException("Script variable identity is ambiguous.");
                var name = FalloutDialogueTopic.Text(field.Data.Span);
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Script variable name is empty.");
                entries.Add(new(entries.Count, entry.Index, entry.Flags, name));
                pending = null;
            }
            else if (field.Signature == "SCRV")
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Compiled reference local extent is invalid.");
                references.Add(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            }
        }
        if (pending is not null) throw new InvalidDataException("Script variable has no source name.");
        var mixed = entries.GroupBy(entry => entry.Index)
            .Where(group => group.Select(entry => entry.StorageFlags).Distinct().Count() > 1)
            .Select(group => group.Key).ToHashSet();
        return new(entries.AsReadOnly(), mixed, references);
    });

    internal static FalloutScriptValue ReadStorageValue(FalloutPluginRecord script, uint slot,
        FalloutScriptLocalKind view, double raw, FalloutScriptValueStore values)
    {
        if (!HasMixedStorage(script, slot)) return values.Read(view, raw);
        RequireMixedView(script, slot, view);
        if (!double.IsFinite(raw)) throw new InvalidDataException("Shared local payload is non-finite.");
        return view == FalloutScriptLocalKind.Form
            ? FalloutScriptValue.Form((uint)BitConverter.DoubleToUInt64Bits(raw)) : raw;
    }

    internal static double WriteStorageValue(FalloutPluginRecord script, uint slot,
        FalloutScriptLocalKind view, double previous, FalloutScriptValue value, FalloutScriptValueStore values,
        string sourcePlugin, string owner, string variableName)
    {
        if (!HasMixedStorage(script, slot)) return values.Write(view, previous, value, sourcePlugin, owner);
        RequireMixedView(script, slot, view);
        if (view == FalloutScriptLocalKind.Form)
        {
            if (value.Kind == FalloutScriptValueKind.Number && value.Number == 0) value = FalloutScriptValue.Form(0);
            if (value.Kind != FalloutScriptValueKind.Form)
                throw new NotSupportedException("Shared reference assignment requires a typed form or literal null.");
            return EncodeSharedReference(value.Number);
        }
        if (value.Kind != FalloutScriptValueKind.Number || !double.IsFinite(value.Number))
            throw new NotSupportedException("Shared numeric assignment requires a finite scalar.");
        var name = variableName.Split('.')[^1];
        var declaration = ReadMetadata(script).FirstOrDefault(entry =>
            entry.Index == slot && entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException("Shared local assignment has no exact named source declaration.");
        if (declaration.StorageFlags == 1 && (value.Number != Math.Truncate(value.Number) ||
            value.Number < int.MinValue || value.Number > int.MaxValue))
            throw new NotSupportedException("Shared integer assignment exceeds its exact Int32 domain.");
        return value.Number;
    }

    internal static double EncodeSharedReference(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > uint.MaxValue || value != Math.Truncate(value))
            throw new InvalidDataException("Shared reference identity exceeds UInt32.");
        return BitConverter.UInt64BitsToDouble((uint)value);
    }

    private static void RequireMixedView(FalloutPluginRecord script, uint slot, FalloutScriptLocalKind view)
    {
        if (view == FalloutScriptLocalKind.Number) return;
        if (view == FalloutScriptLocalKind.Form && HasReferenceView(script, slot)) return;
        throw new NotSupportedException("Shared scalar cell has no admitted extension-handle view.");
    }
}
