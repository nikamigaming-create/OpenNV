using OpenNV.Runtime.Compatibility.NativePlugins;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutNativePluginLocals
{
    internal static NativeNvseLocalAuthority BindCampaign(FalloutPluginStack records, FalloutQuestState quests,
        FalloutReferenceWorld world, FalloutScriptValueStore values, FalloutFormKey caller)
    {
        var record = records.GetEffective(caller);
        if (record.Signature == "QUST")
        {
            var script = FalloutScriptLocals.AttachedScript(records, record) ??
                throw new InvalidDataException("Actual native quest caller has no attached script.");
            var identity = quests.NativeLocalIdentity(caller);
            var locals = quests.NativeLocalStorage(caller) ??
                throw new InvalidDataException("Actual native quest caller has no ordered local owner.");
            void Current()
            {
                if (!ReferenceEquals(quests.NativeSourceRecords, records) || !ReferenceEquals(quests.NativeLocalIdentity(caller), identity) ||
                    !ReferenceEquals(quests.NativeLocalStorage(caller), locals))
                    throw new InvalidOperationException("Native quest caller changed its actual source/local lifetime.");
            }
            return BindOrdered(records, script, values, Owner(caller, script, values, locals, Current,
                () => quests.NativeLocalMutation(caller, identity, locals)));
        }
        var instance = world.Get(caller);
        var source = instance.Script?.Record ?? throw new InvalidDataException("Actual native reference caller has no attached script.");
        var storage = instance.LocalStorage ?? throw new InvalidDataException("Actual native reference caller has no ordered local owner.");
        void RequireCurrent()
        {
            if (!ReferenceEquals(world.NativeSourceRecords, records) || !ReferenceEquals(world.ScriptValues, values) ||
                !ReferenceEquals(world.Get(caller), instance) || !ReferenceEquals(instance.Script?.Record, source) ||
                !ReferenceEquals(instance.LocalStorage, storage))
                throw new InvalidOperationException("Native reference caller changed its actual source/local/value lifetime.");
        }
        return BindOrdered(records, source, values, Owner(caller, source, values, storage, RequireCurrent, null));
    }

    private static FalloutNativePluginOrderedLocalOwner Owner(FalloutFormKey caller, FalloutPluginRecord script,
        FalloutScriptValueStore values, FalloutScriptLocalStorage locals, Action current, Action? changed)
    {
        var kinds = FalloutScriptLocals.ReadStorageKinds(script, FalloutScriptDeclarationAuthority.CompiledVanilla);
        var declarations = locals.Entries.Select(entry => new NativeNvseLocalDeclaration(entry.Index,
            kinds[entry.Index] switch
            {
                FalloutScriptLocalKind.Form => NativeNvseLocalKind.Form,
                FalloutScriptLocalKind.String => NativeNvseLocalKind.String,
                FalloutScriptLocalKind.Array => NativeNvseLocalKind.Array,
                FalloutScriptLocalKind.Number when entry.StorageFlags == 1 => NativeNvseLocalKind.Integer,
                FalloutScriptLocalKind.Number when entry.StorageFlags == 0 => NativeNvseLocalKind.Number,
                _ => throw new NotSupportedException("Ordered campaign local has no admitted native view."),
            }, entry.StorageFlags)).ToArray();
        return new(caller.ToString(), locals.ScriptSha256, values, declarations, locals.ReadEntry,
            changes => locals.PrepareEntries(changes.Select(change => new FalloutScriptLocalCellChange(
                change.Entry, change.Index, change.Before, change.After)).ToArray(), current, changed), current);
    }
}
