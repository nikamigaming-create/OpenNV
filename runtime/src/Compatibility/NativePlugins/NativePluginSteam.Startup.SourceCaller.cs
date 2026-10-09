using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginSteamStartup
{
    private FalloutPlatformStartupInvocation? _sourceEntered;

    internal bool QuerySourceInitialized(FalloutPlatformStartupInvocation invocation)
    {
        EnterSourceCaller(invocation, FalloutPlatformStartupStep.InitializationQuery);
        try
        {
            // This is the same original lazy singleton as the later Main
            // callback child. The early startup does not pump callbacks here.
            if (!Constructed) Construct();
            var result = false;
            Effect(NativePluginSteamServiceStep.SourceReadInitialized, () =>
            {
                if (!_fieldsInitialized || _phase != NativePluginSteamServicePhase.Live)
                    throw new InvalidOperationException("Source initialization query preceded its actual singleton field publication.");
                result = _initialized;
            }, () => result ? 1U : 0U);
            return result;
        }
        catch (Exception failure) { _failure ??= failure; _phase = NativePluginSteamServicePhase.Failed; throw; }
        finally { _sourceEntered = null; }
    }

    internal void WriteSourceIndependentByte(FalloutPlatformStartupInvocation invocation, byte value)
    {
        EnterSourceCaller(invocation, FalloutPlatformStartupStep.IndependentByteWrite);
        try
        {
            if (!Constructed) Construct();
            Effect(NativePluginSteamServiceStep.SourceStoreIndependentByte, () =>
            {
                if (!_fieldsInitialized || _phase != NativePluginSteamServicePhase.Live)
                    throw new InvalidOperationException("Source independent byte setter preceded its singleton constructor return.");
                // The source caller pushes zero. Admit the actual byte
                // producer, not a guessed meaning or a readiness Boolean.
                if (value != 0) throw new InvalidDataException("Early source argument consumer changed its actual zero-store operand.");
                _independentByte = false;
            }, () => _independentByte ? 1U : 0U);
        }
        catch (Exception failure) { _failure ??= failure; _phase = NativePluginSteamServicePhase.Failed; throw; }
        finally { _sourceEntered = null; }
    }

    private void EnterSourceCaller(FalloutPlatformStartupInvocation invocation, FalloutPlatformStartupStep phase)
    {
        invocation.Require(phase);
        if (invocation.Source.Main != SourceDeclaration.Provider.Main || _entered is not null || _sourceEntered is not null ||
            _phase is NativePluginSteamServicePhase.RetirementEntered or NativePluginSteamServicePhase.Retired)
            throw new InvalidOperationException("Source platform caller lost its actual singleton/source lifetime.");
        if (_failure is { } failure) throw new InvalidOperationException("Source platform caller retains its genuine failed prefix.", failure);
        _sourceEntered = invocation;
    }
}
