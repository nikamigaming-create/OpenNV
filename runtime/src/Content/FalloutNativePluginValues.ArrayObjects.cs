using System.Collections.Immutable;
using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginValues
{
    private readonly Func<uint, NativeNvseArrayCreationOwner>? _arrayScriptOwner;
    internal FalloutNativePluginValues(FalloutScriptValueStore store, FalloutPluginStack records,
        NativePluginExecutionDomain domain, NativeNvsePlugin plugin) : this(store)
    {
        _store.Arrays.BindNativeOwnershipSource(records);
        _arrayScriptOwner = pointer =>
        {
            var script = domain.RequireNvseArrayCreator(plugin, pointer);
            var key = records.RuntimeFormKey(script.FormId); var actual = records.GetEffective(key);
            if (actual.IsDeleted || actual.Signature != "SCPT" ||
                !StringComparer.OrdinalIgnoreCase.Equals(script.Authority.SourceSha256,
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actual.ReadData()))))
                throw new InvalidDataException("Array creator's native Script differs from the actual winning source.");
            return new(script.FormId, key.OwnerPlugin, script.Authority.SourceOwner, script.Authority.SourceSha256,
                records.OwnedSource?.StackId ?? throw new NotSupportedException("Array creator has no retained source stack."));
        };
    }
    internal override string ScriptOwner(uint pointer) => (_arrayScriptOwner ?? throw new NotSupportedException(
        "Native string/array creation lacks an actual source Script pointer owner."))(pointer).Plugin;
    internal override uint CreateArrayForScript(int kind, IReadOnlyList<NativeNvseArrayEntry> entries, uint script)
    {
        if (kind is < 0 or > 2) throw new InvalidDataException("Native array constructor kind is invalid.");
        var owner = (_arrayScriptOwner ?? throw new NotSupportedException("Native array creation lacks its actual Script authority."))(script);
        var values = entries.Select(entry => (Key: Input(entry.Key), Value: Input(entry.Value))).ToArray();
        if (values.Any(pair => kind == 2 ? pair.Key.Kind != FalloutScriptValueKind.String : pair.Key.Kind != FalloutScriptValueKind.Number))
            throw new InvalidDataException("Native array constructor key domain is invalid.");
        var array = _store.Arrays.NativeConstructOwned((FalloutScriptArrayKind)kind, owner);
        foreach (var pair in values) _store.Arrays.Set(array, pair.Key, pair.Value);
        return checked((uint)array.Number);
    }
    internal override NativeNvseArrayObjectSnapshot? ArrayObject(uint id)
    {
        if (!ContainsArray(id)) return null;
        var ownership = _store.Arrays.NativeObjectOwnership(id);
        var result = new NativeNvseArrayObjectSnapshot(id, ArrayKind(id), ownership.Mod, ownership.SourceOwner,
            ImmutableArray.CreateRange(ownership.References), ArrayEntries(id));
        NativeNvseArrayObjectLayout.Validate(result); return result;
    }
}
