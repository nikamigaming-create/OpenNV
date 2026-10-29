using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

// Source decoding stays immutable. Gameplay supplies this interface wherever a
// consumer needs the current list rather than its winning file declaration.
internal interface IFalloutFormLists
{
    IReadOnlyList<FalloutFormKey?> Entries(FalloutFormKey list);
}

internal sealed record FalloutFormList(FalloutFormKey Form, string Winner, string Sha256,
    IReadOnlyList<FalloutFormKey?> Members)
{
    internal static FalloutFormList Read(FalloutPluginStack records, FalloutFormKey key)
    {
        var source = records.GetEffective(key);
        if (source.Signature != "FLST") throw new InvalidDataException("Form-list argument is not a winning FLST.");
        return new(key, source.Plugin.Name, Convert.ToHexString(SHA256.HashData(source.ReadData())).ToLowerInvariant(),
            new SourceMembers(records, source));
    }

    // Consumers such as GetEquipped stop at the first match or null entry.
    // Retain immutable fields and validate only visited members; decoding an
    // unvisited malformed tail would change those qualified query semantics.
    private sealed class SourceMembers(FalloutPluginStack records, FalloutPluginRecord source) : IReadOnlyList<FalloutFormKey?>
    {
        private readonly ReadOnlyMemory<byte>[] _fields = source.ReadSubrecords()
            .Where(field => field.Signature == "LNAM").Select(field => field.Data).ToArray();
        public int Count => _fields.Length;
        public FalloutFormKey? this[int index]
        {
            get
            {
                var data = _fields[index];
                if (data.Length != sizeof(uint)) throw new InvalidDataException("FLST member has an invalid FormID extent.");
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
                if (raw == 0) return null;
                var member = source.Plugin.AdjustFormId(raw);
                RequireMember(records, member);
                return member;
            }
        }
        public IEnumerator<FalloutFormKey?> GetEnumerator()
        {
            for (var index = 0; index < Count; index++) yield return this[index];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static IReadOnlyList<FalloutFormKey?> Entries(FalloutPluginStack records, FalloutFormKey key,
        IFalloutFormLists? current = null) => current?.Entries(key) ?? Read(records, key).Members;

    internal static void RequireMember(FalloutPluginStack records, FalloutFormKey form)
    {
        if (records.TryGetEffective(form, out _)) return;
        if (FalloutEngineIdentity.IsPlayerReference(records, form)) return;
        throw new NotSupportedException("Form-list member has no winning form or engine player identity; dynamic references require their live lifetime owner.");
    }
}

// A resource may retain this view, but not a stale expansion of a mutable list.
// The state owner publishes immutable entry arrays; validation is reused until
// that exact array changes, without allocations on ordinary Count/index reads.
internal sealed class FalloutTypedFormList(FalloutPluginStack records, FalloutFormKey list,
    string signature, IFalloutFormLists current) : IReadOnlyList<FalloutFormKey>
{
    private IReadOnlyList<FalloutFormKey?>? _input;
    private FalloutFormKey[] _values = [];
    private IReadOnlyList<FalloutFormKey> Values
    {
        get
        {
            var entries = current.Entries(list);
            if (ReferenceEquals(entries, _input)) return _values;
            var values = entries.Select(entry => entry is { } form && records.GetEffective(form).Signature == signature
                ? form : throw new NotSupportedException($"Form list {list} contains a null or non-{signature} member.")).ToArray();
            if (values.Length == 0) throw new NotSupportedException($"Form list {list} has no {signature} members.");
            _values = values;
            _input = entries;
            return values;
        }
    }
    public int Count => Values.Count;
    public FalloutFormKey this[int index] => Values[index];
    public IEnumerator<FalloutFormKey> GetEnumerator() => Values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
