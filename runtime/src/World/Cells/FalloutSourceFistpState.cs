using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceFistpState : IDisposable
{
    private const string Schema = "opennv-source-calling-thread-FISTP/v1";
    private readonly FalloutMainFrameDeclaration _source;
    private readonly string _stack;
    private readonly Guid _process;
    private FalloutCallingThreadFistpHost? _host;
    private FalloutFistpHostBinding? _lastBinding;
    private FalloutSourceFistpPair? _last;
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _pairs, _conversions, _reentry;
    private bool _entered, _retired;
    internal string? SaveBlocker => _entered ? "actual-source-calling-thread-FISTP-entered" : _last is { Failure: { } error } ? "actual-source-FISTP:" + error : null;
    internal object State => new
    {
        source = _source.Contract,
        executable = _source.ExecutableSha256,
        process = _process,
        bound = _host is not null,
        binding = _lastBinding,
        sequence = _sequence,
        pairs = _pairs,
        nativeConversions = _conversions,
        last = _last,
        cold = _cold,
        retired = _retired,
        saveBlocker = SaveBlocker
    };
    internal FalloutSourceFistpState(FalloutMainFrameDeclaration source, string stack, Guid process,
        FalloutSourceFistpSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new InvalidDataException("FISTP has no actual constructed source process.");
        _source = source; _stack = stack; _process = process;
        if (restore is not null) Restore(restore);
    }
    internal IDisposable Bind(FalloutCallingThreadFistpHost host)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(host); var binding = host.Binding;
        FalloutFistp32Abi.RequireBinding(binding); host.RequireCurrent(binding);
        if (_host is not null || binding.Identity == _lastBinding?.Identity)
            throw new InvalidOperationException("Source FISTP must acquire one fresh actual native-thread construction.");
        _host = host; _lastBinding = binding; Next();
        return new HostLease(this, host, binding);
    }
    private sealed class HostLease(FalloutSourceFistpState owner, FalloutCallingThreadFistpHost host, FalloutFistpHostBinding binding) : IDisposable
    {
        public void Dispose()
        {
            if (!ReferenceEquals(owner._host, host)) return;
            owner.RequireIdle(); host.RequireCurrent(binding);
            owner._host = null; owner.Next();
        }
    }
    internal (int X, int Y) ConvertPair(FalloutSourceFistpContext context, uint first, uint second, Action requireActualCaller)
    {
        RequireIdle(); RequireContext(context); ArgumentNullException.ThrowIfNull(requireActualCaller);
        requireActualCaller();
        if (_last is { Failure: { } retained }) throw new InvalidOperationException("Source FISTP refuses replay after its retained native/source failure: " + retained);
        var entered = Next(); _pairs = checked(_pairs + 1); var fault = _reentry;
        _last = new(context, _host?.Binding, entered, entered, [], false, null); _entered = true;
        try
        {
            var host = _host ?? throw new NotSupportedException("source-FISTP-actual-calling-thread-host-not-bound");
            host.RequireCurrent(_last!.Host!);
            var x = Operand(first); var y = Operand(second);
            Check(); _last = _last! with { Returned = true, Changed = Next() };
            return (x >> 12, y >> 12);

            int Operand(uint bits)
            {
                Check(); var began = Next();
                _last = _last! with { Changed = began, Operands = [.. _last!.Operands, new(bits, null, null, null, began, null, null)] };
                var ordinal = host.ObservationOrdinal;
                try
                {
                    var actual = host.Convert(bits, FalloutSourceMainFamily.GridOperation(_source.ExecutableSha256));
                    var values = _last!.Operands.ToArray();
                    values[^1] = values[^1] with { Native = actual };
                    _last = _last! with { Changed = Next(), Operands = values };
                    if (actual.Outcome != FalloutFistpOutcome.Converted)
                    {
                        FalloutFistp32Semantics.RequireRefusal(actual);
                        throw new NotSupportedException("actual-calling-thread-FISTP-" + actual.Outcome);
                    }
                    _conversions = checked(_conversions + 1);
                    var semantic = FalloutFistp32Semantics.RequireConverted(actual);
                    Check(); values = _last!.Operands.ToArray();
                    var returned = Next(); values[^1] = values[^1] with { Integer = semantic.Integer, Grid = semantic.Integer >> 12, Returned = returned };
                    _last = _last! with { Operands = values, Changed = returned };
                    return semantic.Integer;
                }
                catch (Exception error)
                {
                    var values = _last!.Operands.ToArray();
                    // A malformed native return is still retained as observed
                    // data. An earlier same-value instruction is never reused.
                    var observed = host.ObservationOrdinal > ordinal && host.LastObservation is { } native &&
                        native.Operation == FalloutSourceMainFamily.GridOperation(_source.ExecutableSha256) ? native : null;
                    values[^1] = values[^1] with { Native = values[^1].Native ?? observed, Failure = Message(error) };
                    _last = _last! with { Changed = Next(), Operands = values }; throw;
                }
            }
            void Check()
            {
                requireActualCaller();
                if (_reentry != fault || !ReferenceEquals(_host, host) || _last!.Context != context)
                    throw new InvalidOperationException("FISTP operand lost its actual source invocation/native-thread lease.");
                host.RequireCurrent(_last!.Host!);
            }
        }
        catch (Exception error) { _last = _last! with { Changed = Next(), Failure = Message(error) }; throw; }
        finally { _entered = false; }
    }
    private void RequireContext(FalloutSourceFistpContext context)
    {
        ValidateContext(context);
        if (context.Process != _process) throw new InvalidDataException("Calling-thread FISTP belongs to another actual source-process epoch.");
    }
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (!_entered) return;
        _reentry = checked(_reentry + 1);
        throw new InvalidOperationException("Calling-thread FISTP cannot reenter capture, binding, conversion or retirement.");
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static string Message(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    public void Dispose()
    {
        if (_retired) return; RequireIdle();
        if (_host is not null) throw new InvalidOperationException("Source FISTP still owns a living actual native-thread provider lease.");
        _retired = true;
        // Faults, actual native flags and the completed prefix remain visible.
    }
}
