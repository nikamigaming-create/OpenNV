using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed class FalloutQueuedReferencePriority : IDisposable
{
    private const string Schema = "opennv-source-queued-priority/v1";
    private readonly FalloutMainFrameDeclaration _source;
    private readonly string _stack;
    private readonly Guid _process = Guid.NewGuid();
    private FalloutQueuedPriorityCache? _cache;
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _disposed, _callback;
    internal FalloutQueuedReferencePriority(FalloutMainFrameDeclaration source, string stack,
        FalloutQueuedPrioritySnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); _source = source; _stack = stack;
        if (restore is not null)
        {
            Validate(restore);
            if (restore.Contract != source.Contract || restore.Stack != stack || restore.CapturedProcess == _process)
                throw new InvalidDataException("Source priority cache differs from its selected new process.");
            _sequence = restore.Sequence;
            // A native pointer cache never survives a new process. Its old
            // source identity is retained diagnostically by the handoff, not
            // equated with a newly allocated CELL bearing the same FormKey.
            _cold = new(restore.CapturedProcess, _process, Next());
        }
    }
    internal int Read(FalloutQueuedPriorityCell cell, Func<FalloutQueuedPriorityInputs> inputs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(inputs);
        if (_callback)
        {
            _callbackFault = checked(_callbackFault + 1);
            throw new InvalidOperationException("Source priority input reentered its cache.");
        }
        RequireCell(cell);
        if (_cache is { } cached && cached.Cell.Instance == cell.Instance)
        {
            if (cached.Cell != cell) throw new InvalidDataException("Same source CELL instance changed its immutable priority fields.");
            return cached.Priority;
        }
        _callback = true;
        FalloutQueuedPriorityInputs value;
        var faults = _callbackFault;
        try
        {
            value = inputs() ?? throw new InvalidDataException("Source priority caller has no actual inputs.");
            if (faults != _callbackFault) throw new InvalidOperationException("Priority callback caught its actual cache reentry refusal.");
        }
        catch { _callback = false; throw; }
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value.Owner);
            ArgumentNullException.ThrowIfNull(value.GridDiameter);
            int x, y;
            if (cell.Interior) { x = Half(); y = Half(); }
            else if (value.Position is { } position)
            {
                var rounding = position.Rounding.Require();
                x = unchecked(cell.X!.Value - (ConvertSourcePosition(position.XBits, rounding) >> 12) + Half());
                y = unchecked(cell.Y!.Value - (ConvertSourcePosition(position.YBits, rounding) >> 12) + Half());
            }
            else
            {
                x = unchecked(cell.X!.Value + Half() - value.GridX.Require());
                y = unchecked(cell.Y!.Value + Half() - value.GridY.Require());
            }
            x = unchecked(x - Half()); y = unchecked(y - Half());
            var signed = x > y ? x : y;
            var magnitude = signed < 0 ? unchecked(-signed) : signed;
            var wrapped = unchecked((byte)(magnitude * 10 + 10));
            var priority = wrapped <= 20 ? 1 : 3;
            _cache = new(cell, priority, Next());
            return priority;
            int Half()
            {
                var result = value.GridDiameter().Require();
                if (_callbackFault != faults) throw new InvalidOperationException("Priority setting producer caught an actual source-cache reentry refusal.");
                return unchecked((int)(result >> 1));
            }
        }
        finally { _callback = false; }
    }
    // Float32 bits and the original calling thread's actual rounding mode are
    // independent inputs. Nonfinite/out-of-range FISTP produces Int32.MinValue.
    internal static int ConvertSourcePosition(uint bits, FalloutSourceFloatRounding rounding)
    {
        if (!Enum.IsDefined(rounding)) throw new NotSupportedException("Original floating-point conversion mode is unowned.");
        var input = BitConverter.UInt32BitsToSingle(bits);
        var rounded = rounding switch
        {
            FalloutSourceFloatRounding.NearestEven => Math.Round((double)input, MidpointRounding.ToEven),
            FalloutSourceFloatRounding.Down => Math.Floor((double)input),
            FalloutSourceFloatRounding.Up => Math.Ceiling((double)input),
            FalloutSourceFloatRounding.TowardZero => Math.Truncate((double)input),
            _ => throw new NotSupportedException("Original floating-point conversion mode is unowned.")
        };
        return !double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue ? int.MinValue : (int)rounded;
    }
    internal static void RequireCell(FalloutQueuedPriorityCell cell)
    {
        if (cell is null || cell.Source is null || cell.Instance == Guid.Empty ||
            cell.Source.Cell.ObjectId == 0 || string.IsNullOrWhiteSpace(cell.Source.Cell.OwnerPlugin) ||
            cell.Source.Sha256 is not { Length: 64 } || !cell.Source.Sha256.All(Uri.IsHexDigit) ||
            (cell.X is null) != (cell.Y is null) || !cell.Interior && cell.X is null)
            throw new InvalidDataException("Queued priority has no exact source CELL instance/coordinate owner.");
    }
    internal FalloutQueuedPrioritySnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_callback) throw new NotSupportedException("Source priority inputs are currently entered.");
        return new(Schema, _source.Contract, _stack, _process, _sequence, _cache, _cold);
    }
    internal string? SaveBlocker => _callback ? "actual-source-priority-inputs-entered" : null;
    internal object State => new
    {
        contract = _source.Contract,
        process = _process,
        sequence = _sequence,
        cache = _cache,
        cold = _cold,
        saveBlocker = SaveBlocker
    };
    internal static void Validate(FalloutQueuedPrioritySnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Contract is not { Length: 64 } || !saved.Contract.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0)
            throw new InvalidDataException("Source priority snapshot lost its actual process cache.");
        if (saved.Cache is { } cached)
        {
            RequireCell(cached.Cell);
            if (cached.Priority is not (1 or 3) || cached.Changed < 1 || cached.Changed > saved.Sequence)
                throw new InvalidDataException("Source priority cache contains an impossible selected output.");
        }
        if (saved.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != saved.CapturedProcess ||
            cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > saved.Sequence))
            throw new InvalidDataException("Source priority cache lost its new-process invalidation.");
    }
    private long Next() => _sequence = checked(_sequence + 1);
    public void Dispose()
    {
        if (_disposed) return;
        if (_callback) throw new NotSupportedException("Source priority callback still owns the cache.");
        _disposed = true; _cache = null;
    }
}
