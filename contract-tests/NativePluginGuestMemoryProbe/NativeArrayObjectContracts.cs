using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.Content;

internal static class NativeArrayObjectContracts
{
    internal static void Run()
    {
        Require(NativeNvseArrayAccessor.Read("ScriptTokenGetArray", "pointer32", "NVSEArrayVarInterface::Array") == NativeNvseArrayAccessorKind.PublicOpaqueId,
            "Exact public opaque array return acquired an internal layout.");
        Require(NativeNvseArrayAccessor.Read("ScriptTokenGetArrayID", "uint32", null) == NativeNvseArrayAccessorKind.Id, "UInt32 array return changed.");
        Reject<NotSupportedException>(() => NativeNvseArrayAccessor.Read("ScriptTokenGetArray", "pointer32", "NVSEArrayVar"));
        Reject<NotSupportedException>(() => NativeNvseArrayAccessor.Read("ScriptTokenGetArrayID", "pointer32", "NVSEArrayVarInterface::Array"));
        Reject<NotSupportedException>(() => NativeNvseArrayAccessor.Read("ScriptTokenGetArray", "pointer32", "ArrayVar"));
        var packed = new NativeNvseArrayObjectSnapshot(19, 0, 7, "authored-layout", [7, 7, 4],
            [new(NativeNvseElementValue.Numeric(0), NativeNvseElementValue.Numeric(3.5)),
             new(NativeNvseElementValue.Numeric(1), NativeNvseElementValue.Reference(NativeNvseElementType.Array, 21)),
             new(NativeNvseElementValue.Numeric(2), NativeNvseElementValue.String("text"u8))]);
        NativeNvseArrayObjectLayout.Validate(packed);
        NativeNvseArrayObjectLayout.Validate(packed with { Kind = 1, Entries =
            [new(NativeNvseElementValue.Numeric(-1.25), NativeNvseElementValue.Reference(NativeNvseElementType.Form, 0x01008001)),
             new(NativeNvseElementValue.Numeric(2.5), NativeNvseElementValue.Numeric(6))] });
        NativeNvseArrayObjectLayout.Validate(packed with { Kind = 2, Entries =
            [new(NativeNvseElementValue.String("A"u8), NativeNvseElementValue.Numeric(1)),
             new(NativeNvseElementValue.String("b"u8), NativeNvseElementValue.String([]))] });
        Reject<InvalidDataException>(() => NativeNvseArrayObjectLayout.Validate(packed with { Id = 0 }));
        Reject<InvalidDataException>(() => NativeNvseArrayObjectLayout.Validate(packed with { Kind = 3 }));
        Reject<InvalidDataException>(() => NativeNvseArrayObjectLayout.Validate(packed with { ReferringMods = default }));
        Reject<InvalidDataException>(() => NativeNvseArrayObjectLayout.Validate(packed with { Entries = [packed.Entries[1]] }));
        Reject<InvalidDataException>(() => NativeNvseArrayObjectLayout.Validate(packed with { Kind = 1, Entries = [packed.Entries[1], packed.Entries[0]] }));
        Reject<NotSupportedException>(() => NativeNvseArrayObjectLayout.Validate(packed with { Kind = 2, Entries =
            [new(NativeNvseElementValue.String([0xe9]), NativeNvseElementValue.Numeric(1))] }));

        // Real shared storage, independent from the native snapshot validator.
        // Missing original Script creation must stay refused, even though all
        // public array operations and actual root/nested storage are present.
        var store = new FalloutScriptArrayStore();
        var parent = store.NativeConstruct(FalloutScriptArrayKind.Array); var child = store.NativeConstruct(FalloutScriptArrayKind.Array);
        store.Set(child, 0, 10); store.Set(parent, 0, child); store.Set(parent, 1, child);
        store.SetRoot("actual:one", parent, "authored.esm"); store.SetRoot("actual:two", child, "authored.esm");
        var source = store.Capture();
        var childRows = source.Single(value => value.Id == child.Number).NativeOwnership!.References;
        Require(childRows.Count == 3 && childRows.Select(value => value.Order).SequenceEqual(childRows.Select(value => value.Order).Order()),
            "Shared nested/root reference occurrences were collapsed or reordered.");
        Reject<NotSupportedException>(() => store.NativeObjectOwnership((uint)parent.Number));
        store.Erase(parent, 0);
        var reindexed = store.Capture().Single(value => value.Id == child.Number).NativeOwnership!.References;
        Require(reindexed.Count == 2 && reindexed.Single(value => value.Parent is not null).ElementKey!.Number == 0,
            "Packed erase lost its surviving independent nested edge/order.");
        store.Resize(parent, 3, child); store.Resize(parent, 1, 0);
        Require(store.Capture().Single(value => value.Id == child.Number).NativeOwnership!.References.Count == 2,
            "Resize retained removed nested reference occurrences.");
        var cold = store.Capture(); var restored = new FalloutScriptArrayStore(); restored.Restore(store.LastId, cold);
        restored.SetRoot("actual:one", restored.Reference(parent.Number)); restored.SetRoot("actual:two", restored.Reference(child.Number));
        restored.ValidateRestoredRoots();
        Require(restored.Capture().Single(value => value.Id == child.Number).NativeOwnership!.References
            .SequenceEqual(cold.Single(value => value.Id == child.Number).NativeOwnership!.References), "Cold graph reconstruction replayed reference assignments.");
        var corrupt = cold.Select(value => value.Id == child.Number ? value with { NativeOwnership = value.NativeOwnership! with
            { References = value.NativeOwnership!.References.Select(row => row with { Key = "unrelated" }).ToArray() } } : value).ToArray();
        Reject<InvalidDataException>(() => new FalloutScriptArrayStore().Restore(store.LastId, corrupt));
        var before = restored.Capture();
        Reject<InvalidDataException>(() => restored.Restore(store.LastId, corrupt));
        Require(restored.Get(restored.Reference(parent.Number), 0).Number == child.Number && restored.Capture().Count == before.Count,
            "Rejected cold native metadata mutated the real array graph.");
        using (restored.BeginExecution()) { restored.SetRoot("actual:one", FalloutScriptValue.Array(0)); restored.SetRoot("actual:two", FalloutScriptValue.Array(0)); }
        Require(restored.Count == 0, "Actual unreachable array graph did not collect.");
        Console.WriteLine("OPENNV_INTERNAL_ARRAY_SOURCE_PASS nativeObjects=unexecuted originalLoad=unaccepted internalVirtualMutation=unowned");
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected array refusal " + typeof(T).Name); }
}
