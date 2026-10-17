using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceFistpState
{
    internal FalloutModelKeyFistpCall ConvertModelKey(FalloutReferenceModelDataInvocation load, uint input,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(load);
        if (load.ThreadId != Environment.CurrentManagedThreadId || load.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(input)) || BitConverter.UInt32BitsToSingle(input) <= 0)
            throw new InvalidDataException("Model scale conversion lost its actual positive finite current load scalar.");
        if (_last is { Failure: not null } || _lastModelKey is { Failure: not null })
            throw new InvalidOperationException("Model scale conversion cannot replay across a retained source float failure.");
        _modelKeys = checked(_modelKeys + 1); var entered = Next(); var fault = _reentry;
        _lastModelKey = new(_process, load, input, _host?.Binding, entered, entered, null, null, false, null); _entered = true;
        var host = _host; var ordinal = host?.ObservationOrdinal;
        try
        {
            if (host is null) throw new NotSupportedException("source-model-key-actual-calling-thread-FISTP-host-unbound");
            host.RequireCurrent(_lastModelKey.Host!);
            var actual = host.Convert(input, FalloutFistpOperation.ScaledHundred);
            _lastModelKey = _lastModelKey with { Native = actual, Changed = Next() };
            if (actual.Outcome != FalloutFistpOutcome.Converted)
            { FalloutFistp32Semantics.RequireRefusal(actual); throw new NotSupportedException("source-model-key-FISTP-" + actual.Outcome); }
            _modelKeyConversions = checked(_modelKeyConversions + 1);
            var result = FalloutFistp32Semantics.RequireConverted(actual);
            requireActual(load); host.RequireCurrent(_lastModelKey.Host!);
            if (_reentry != fault || !ReferenceEquals(host, _host))
                throw new InvalidOperationException("Model-key float instruction lost its actual calling-thread lease or swallowed reentry.");
            return _lastModelKey = _lastModelKey with { Integer = result.Integer, Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            var observed = host is not null && ordinal is not null && host.ObservationOrdinal > ordinal &&
                host.LastObservation is { Operation: FalloutFistpOperation.ScaledHundred } native ? native : null;
            _lastModelKey = _lastModelKey with { Native = _lastModelKey.Native ?? observed, Failure = Message(error), Changed = Next() }; throw;
        }
        finally { _entered = false; }
    }

    private static void ValidateModelKeyFistp(FalloutSourceFistpSnapshot saved)
    {
        if (saved.ModelKeys < 0 || saved.ModelKeyConversions < 0 || saved.ModelKeyConversions > saved.ModelKeys ||
            (saved.ModelKeys == 0) != (saved.LastModelKey is null))
            throw new InvalidDataException("Cold FISTP omitted its actual model-key conversion count or last call.");
        if (saved.LastModelKey is not { } call) return;
        if (call.Process == Guid.Empty || call.Process != saved.CapturedProcess && saved.ColdHandoff is null || call.Load is null ||
            call.Load.Call == Guid.Empty || call.Load.ThreadId < 1 || call.Entered < 1 || call.Changed <= call.Entered || call.Changed > saved.Sequence ||
            call.Returned != (call.Integer is not null) || call.Returned != (call.Failure is null))
            throw new InvalidDataException("Cold model-key FISTP changed its actual invocation or failed prefix.");
        if (call.Host is { } host) FalloutFistp32Abi.RequireBinding(host);
        if (call.Returned)
        {
            if (saved.ModelKeyConversions < 1) throw new InvalidDataException("Cold model-key return omitted its actual native instruction count.");
            if (call.Native is not { } native || call.Host is null || native.NativeProcess != call.Host.Construction.NativeProcess ||
                native.NativeThread != call.Host.Construction.NativeThread)
                throw new InvalidDataException("Cold model-key integer lost its real native calling thread.");
            FalloutFistp32Abi.RequireHeader(native, FalloutFistpOperation.ScaledHundred, call.Input);
            if (FalloutFistp32Semantics.RequireConverted(native).Integer != call.Integer)
                throw new InvalidDataException("Cold model-key integer differs from its actual multiply/store/FISTP child.");
        }
    }
}
