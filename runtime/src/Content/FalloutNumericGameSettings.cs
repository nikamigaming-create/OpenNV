using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace OpenNV.Runtime.Content;

// Numeric settings are mutable engine state for this loaded stack. They are
// deliberately absent from campaign snapshots; a new stack reads owned defaults.
internal sealed class FalloutNumericGameSettings(FalloutPluginStack records, RuntimeLiveContentSource? ownedSource = null)
{
    private sealed record Declaration(string Name, char Kind, double Value);
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, IReadOnlyDictionary<string, float>> FloatDefaults = new();
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, IReadOnlyDictionary<string, uint>> IntegerDefaults = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, Declaration?> _declarations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _retainedConsumers = new(StringComparer.OrdinalIgnoreCase);
    private ILookup<string, FalloutPluginRecord>? _winning;

    internal long Revision { get; private set; }
    internal double Get(string name) { lock (_sync) return !name.Contains(':') && Resolve(name) is { } source ? Value(source) : -1; }
    internal double Read(string name) { lock (_sync) return Value(Require(name)); }
    internal float Float(string name)
    {
        lock (_sync)
        {
            var source = Require(name);
            if (source.Kind != 'f') throw new InvalidDataException($"Game setting {name} is not a Float32.");
            return (float)Value(source);
        }
    }
    internal uint IntegerBits(string name)
    {
        lock (_sync)
        {
            var source = Require(name);
            return source.Kind switch
            {
                'i' or 'b' => unchecked((uint)(int)Value(source)),
                'u' => (uint)Value(source),
                _ => throw new InvalidDataException($"Game setting {name} is not an integer payload."),
            };
        }
    }

    internal bool Set(string name, double number)
    {
        // NVSE extracts the numeric operand as Float32 before storing it in
        // the declared setting type. Reject non-finite/undefined conversions.
        var input = (float)number;
        if (!double.IsFinite(number) || !float.IsFinite(input)) throw new InvalidDataException("Numeric game setting must be finite Float32.");
        lock (_sync)
        {
            if (name.Contains(':') || Resolve(name) is not { } source) return false;
            double value = source.Kind switch
            {
                'f' => input,
                'b' => input == 0 ? 0 : 1,
                'i' when Math.Truncate((double)input) is >= int.MinValue and <= int.MaxValue => Math.Truncate((double)input),
                'u' when Math.Truncate((double)input) is >= 0 and <= uint.MaxValue => Math.Truncate((double)input),
                _ => throw new InvalidDataException($"Numeric game setting {name} exceeds its declared storage."),
            };
            if (value == Value(source)) return true;
            if (_retainedConsumers.TryGetValue(source.Name, out var consumers))
                throw new NotSupportedException($"Game setting {source.Name} requires live refresh for retained consumers: {string.Join(",", consumers.Order(StringComparer.Ordinal))}.");
            _overrides[source.Name] = value;
            Revision++;
            return true;
        }
    }

    // A consumer that copies a setting into retained state must either refresh
    // it or expose this boundary before a script can invalidate that copy.
    internal void RequireRetainedConsumer(string name, string owner)
    {
        lock (_sync)
        {
            var source = Require(name);
            if (!_retainedConsumers.TryGetValue(source.Name, out var consumers))
                _retainedConsumers.Add(source.Name, consumers = new(StringComparer.Ordinal));
            consumers.Add(owner);
        }
    }

    internal object State
    {
        get
        {
            lock (_sync) return new
            {
                revision = Revision,
                defaultsSource = ownedSource?.StackId,
                overrides = _overrides.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new { name = pair.Key, value = pair.Value }).ToArray(),
                retainedConsumerBoundaries = _retainedConsumers.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new { name = pair.Key, owners = pair.Value.Order(StringComparer.Ordinal).ToArray() }).ToArray(),
                persistence = "loaded-stack-session-only;not-save-baked",
                behavior = "setting-storage-and-bound-consumers;unimplemented-gameplay-consumers-remain-unbound",
            };
        }
    }

    private double Value(Declaration source) => _overrides.GetValueOrDefault(source.Name, source.Value);
    private Declaration Require(string name) => Resolve(name) ?? throw new NotSupportedException($"Owned numeric game setting is unbound: {name}.");
    private Declaration? Resolve(string name)
    {
        if (_declarations.TryGetValue(name, out var cached)) return cached;
        if (_winning is null)
        {
            // Settings share a case-insensitive name registry. Separate FormIDs
            // can declare the same setting; the last source declaration wins.
            // Runtime FormID and first registration order are not load order
            // when a later plugin overrides an older FormID.
            var order = records.Plugins.ToDictionary(plugin => plugin.Plugin.Name,
                plugin => plugin.LoadOrderIndex, StringComparer.OrdinalIgnoreCase);
            _winning = records.EffectiveRecords("GMST").OrderBy(record => order[record.Plugin.Name])
                .ThenBy(record => record.HeaderOffset).ToLookup(record =>
                    Name(record.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span), StringComparer.OrdinalIgnoreCase);
        }
        var winner = _winning[name].LastOrDefault();
        Declaration? declaration = null;
        if (winner is not null)
        {
            var fields = winner.ReadSubrecords().ToArray();
            var identity = Name(fields.Single(field => field.Signature == "EDID").Data.Span);
            if (identity.Length == 0) throw new InvalidDataException("Game setting has no EDID.");
            var kind = char.ToLowerInvariant(identity[0]);
            if (kind is 'f' or 'i' or 'b' or 'u')
            {
                var data = fields.Single(field => field.Signature == "DATA").Data.Span;
                if (data.Length != 4) throw new InvalidDataException($"Numeric GMST {identity} has an invalid payload extent.");
                double number = kind switch
                {
                    'f' => (double)BinaryPrimitives.ReadSingleLittleEndian(data),
                    'u' => BinaryPrimitives.ReadUInt32LittleEndian(data),
                    _ => BinaryPrimitives.ReadInt32LittleEndian(data),
                };
                if (!double.IsFinite(number)) throw new InvalidDataException($"Numeric GMST {identity} is non-finite.");
                declaration = new(identity, kind, number);
            }
        }
        else if (ownedSource is { } content)
        {
            if (content.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
                throw new NotSupportedException("This engine's executable numeric-setting layout has not been admitted.");
            var executable = Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe");
            if (name.Length != 0 && char.ToLowerInvariant(name[0]) == 'f' && FloatDefaults.GetValue(content, _ => FalloutExecutableStringTable.ReadFloatDefaults(executable)).TryGetValue(name, out var number))
                declaration = new(name, 'f', number);
            else if (name.Length != 0 && char.ToLowerInvariant(name[0]) == 'i' && IntegerDefaults.GetValue(content, _ => FalloutExecutableStringTable.ReadIntegerDefaults(executable)).TryGetValue(name, out var bits))
                declaration = new(name, 'i', unchecked((int)bits));
        }
        _declarations.Add(name, declaration);
        return declaration;
    }

    private static string Name(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (bytes.Length == 0 || end != bytes.Length - 1) throw new InvalidDataException("Game-setting EDID is not null-terminated.");
        return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes[..end]);
    }
}
