using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainConsoleSubmissionPhase { Entered, Normalized, SuppressionCommitted, ExecutionEntered, Returned, Failed }
internal sealed record FalloutMainConsoleSubmissionReceipt(Guid Identity, Guid SourceProcess, string Producer,
    long Entered, long Changed, string InputSha256, string? NormalizedSha256, FalloutMainConsoleSubmissionPhase Phase,
    byte SuppressionBefore, byte SuppressionAfter, bool? ExecutionResult, string? FailureType = null, string? Error = null);
internal sealed record FalloutMainUtilityCommandSnapshot(string Schema, FalloutMainUtilityCommandSource Source,
    FalloutConsoleActivitySource ConsoleSource, string Stack, Guid CapturedProcess, long Changed, long Constructed, byte Suppression,
    long Submissions, FalloutMainConsoleSubmissionReceipt? LastSubmission, long DiagnosticCalls,
    long LastDiagnostic, int? LastDiagnosticId, uint? LastDiagnosticReturn);

// This invocation is minted by the actual source process after checking its
// controller publication and exact input event. It never certifies parsing or
// an outcome from delivered input alone.
internal sealed class FalloutMainConsoleSubmission(FalloutActorProcessRuntimeState owner, Guid identity,
    byte[] normalized, string producer)
{
    internal Guid Identity { get; } = identity;
    internal ReadOnlyMemory<byte> Normalized { get; } = normalized;
    internal string Producer { get; } = producer;
    internal void Require(FalloutActorProcessRuntimeState actualOwner) => owner.RequireConsoleSubmission(this, actualOwner);
}

internal sealed record FalloutMainConsoleInput(string Producer, uint Event, FalloutConsoleActivitySample Activity,
    ReadOnlyMemory<byte> EditBytes);

internal enum FalloutPlatformStartupStep { InitializationQuery, Arguments, IndependentByteWrite, Returned, Failed }
internal sealed record FalloutPlatformStartupReceipt(Guid Identity, Guid SourceProcess, string Producer,
    string ArgumentIdentity, long Entered, long Changed, FalloutPlatformStartupStep Step,
    bool? InitializedResult, int ArgumentsVisited, IReadOnlyList<int> MatchedArguments,
    string? FailureType = null, string? Error = null);
internal sealed record FalloutSourceLaunchArguments(string Producer, string ConfigurationIdentity,
    IReadOnlyList<string?> Arguments)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Producer) || !FalloutAdvancementRuntimeReceipt.Digest(ConfigurationIdentity) ||
            Arguments is null || Arguments.Count > 65536 || Arguments.Any(argument => argument?.Contains('\0') == true || argument?.Length > 32767))
            throw new InvalidDataException("Actual selected source launch arguments omitted their configuration/vector identity.");
    }
    internal string Identity
    {
        get
        {
            Validate();
            return FalloutAdvancementRuntimeReceipt.Hash(ConfigurationIdentity + "\0" + Producer + "\0" +
                string.Join("\0", Arguments.Select(argument => argument is null ? "NULL" : "VALUE:" + argument)));
        }
    }
}

internal sealed class FalloutPlatformStartupInvocation(FalloutActorProcessRuntimeState owner, Guid identity,
    FalloutPlatformStartupArgumentSource source)
{
    internal FalloutActorProcessRuntimeState Owner { get; } = owner;
    internal Guid Identity { get; } = identity;
    internal FalloutPlatformStartupArgumentSource Source { get; } = source;
    internal void Require(FalloutPlatformStartupStep step) => Owner.RequirePlatformStartup(this, step);
}
internal interface IFalloutPlatformStartupConsumer
{
    FalloutMainUtilitySource Source { get; }
    string Owner { get; }
    bool QuerySourceInitialized(FalloutPlatformStartupInvocation invocation);
    void WriteSourceIndependentByte(FalloutPlatformStartupInvocation invocation, byte value);
}
