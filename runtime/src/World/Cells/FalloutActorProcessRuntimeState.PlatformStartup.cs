using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutPlatformStartupInvocation? _platformStartupEntered;
    private FalloutPlatformStartupReceipt? _platformStartup;
    internal object? PlatformStartupState => _platformStartup;
    internal bool PlatformStartupReturned => _platformStartup is { Step: FalloutPlatformStartupStep.Returned } && _platformStartupEntered is null;
    internal string? PlatformStartupSaveBlocker => _platformStartupEntered is not null ? "source-platform-startup-entered" :
        _platformStartup is { Step: FalloutPlatformStartupStep.Failed } failed ? "source-platform-startup:" + failed.Error : null;
    internal void ExecuteSourcePlatformStartup(FalloutPlatformStartupArgumentSource source, FalloutSourceLaunchArguments arguments,
        IFalloutPlatformStartupConsumer actualService)
    {
        RequireNotBusy(); source.Validate(); arguments.Validate(); ArgumentNullException.ThrowIfNull(actualService);
        if (_platformStartup is not null || source.Main != MainUtilitySource || actualService.Source != MainUtilitySource ||
            string.IsNullOrWhiteSpace(actualService.Owner) || _scriptCallerInvocation is not null ||
            _scriptCallerLastDeliveredFrame is not null || _scriptCallerLast?.SourceProcess == _process)
            throw new InvalidOperationException("Original early platform startup cannot replace or replay its actual source caller.");
        // Retain the actual launch vector before SDK callbacks can run. Cold
        // may preserve old Main history, but this is the new process's genuine
        // startup; it does not replay an old game-frame invocation.
        var retainedArguments = arguments with { Arguments = Array.AsReadOnly(arguments.Arguments.ToArray()) };
        var identity = Guid.NewGuid(); var sequence = Next();
        _platformStartup = new(identity, _process, actualService.Owner, retainedArguments.Identity, sequence, sequence,
            FalloutPlatformStartupStep.InitializationQuery, null, 0, []);
        _platformStartupEntered = new(this, identity, source);
        try
        {
            var returned = actualService.QuerySourceInitialized(_platformStartupEntered);
            // The original startup ignores this returned scalar, even false.
            _platformStartup = _platformStartup with { Changed = Next(), Step = FalloutPlatformStartupStep.Arguments, InitializedResult = returned };
            for (var index = 0; index < retainedArguments.Arguments.Count; index++)
            {
                _platformStartup = _platformStartup with { Changed = Next(), ArgumentsVisited = checked(index + 1) };
                if (!source.Matches(retainedArguments.Arguments[index])) continue;
                _platformStartup = _platformStartup with
                {
                    Changed = Next(),
                    Step = FalloutPlatformStartupStep.IndependentByteWrite,
                    MatchedArguments = [.. _platformStartup.MatchedArguments, index]
                };
                actualService.WriteSourceIndependentByte(_platformStartupEntered, 0);
                _platformStartup = _platformStartup with { Changed = Next(), Step = FalloutPlatformStartupStep.Arguments };
            }
            _platformStartup = _platformStartup with { Changed = Next(), Step = FalloutPlatformStartupStep.Returned };
        }
        catch (Exception error)
        {
            _platformStartup = _platformStartup! with
            {
                Changed = Next(),
                Step = FalloutPlatformStartupStep.Failed,
                FailureType = error.GetType().FullName,
                Error = Message(error)
            };
            throw;
        }
        finally { _platformStartupEntered = null; }
    }
    internal void RequirePlatformStartup(FalloutPlatformStartupInvocation invocation, FalloutPlatformStartupStep step)
    {
        RequireNotBusy();
        if (!ReferenceEquals(invocation, _platformStartupEntered) || !ReferenceEquals(invocation.Owner, this) ||
            _platformStartup?.Identity != invocation.Identity || _platformStartup.SourceProcess != _process ||
            _platformStartup.Step != step || invocation.Source.Main != MainUtilitySource)
            throw new InvalidDataException("Platform service lost its genuine entered early source caller/phase.");
    }
}
