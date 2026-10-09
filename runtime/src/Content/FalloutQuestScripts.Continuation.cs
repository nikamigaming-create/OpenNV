using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuestScriptPendingCommand(string SourceSha256, int Statement, bool GameMode)
{
    internal void Validate(string? error)
    {
        if (error is null || Statement < 0 || SourceSha256.Length != 64 || !SourceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Saved stopped script instruction is invalid.");
    }
}

internal sealed record FalloutQuestScriptContinuationReceipt(string Error, string SourceSha256,
    int Statement, long Invocation, string Disposition, bool LegacySourceUnverified = false)
{
    internal void Validate(long invocations)
    {
        if (string.IsNullOrWhiteSpace(Error) || SourceSha256.Length != 64 || !SourceSha256.All(Uri.IsHexDigit) ||
            Statement < 0 || Invocation <= 0 || Invocation > invocations + 1 ||
            Disposition is not ("tail-completed-prefix-retained" or "tail-stopped-prefix-retained"))
            throw new InvalidDataException("Saved script continuation receipt is invalid.");
    }
}

internal sealed partial class FalloutQuestScripts
{
    private static string ScriptHash(Instance instance) => FalloutScriptSourceIdentity.Hash(instance.Script);

    private static bool MissingCommand(string error) => Regex.IsMatch(error,
        @"^Reached (?:native script|object-script) command \S+ \(\d+ arguments\) has no owner\.$",
        RegexOptions.CultureInvariant);

    private void RetainFailure(Instance instance, Exception error, FalloutGameModeProgram program, bool gameMode)
    {
        instance.Error = error.Message;
        instance.PendingCommand = MissingCommand(error.Message) && program.LastStatement >= 0
            ? new(ScriptHash(instance), program.LastStatement, gameMode) : null;
        _unbound[instance.Quest.FormKey] = error.Message;
    }

    private void ContinueMissingCommand(Instance instance, FalloutQuestScriptHost? host)
    {
        FalloutQuestScriptAuthority.RequireSourceExecution(instance.Script);
        var error = instance.Error!;
        // Admission is capability-specific, never quest/location-specific.
        // Non-missing failures and unowned operations remain latched. The
        // newly bound command still validates its reference, SOUN and media.
        if (!Sounds.IsBound || !Regex.IsMatch(error,
            @"^Reached (?:native script|object-script) command (?:\w+\.)?playSound3D \(1 arguments\) has no owner\.$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            instance.PendingCommand is { GameMode: false }) return;
        bool Fixed(string token) => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? double.IsFinite(number) :
            token.Length >= 2 && token[0] == '"' && token[^1] == '"' ||
            FalloutScriptBindings.IsPlayer(token) && instance.Bindings.HasPlayerReference ||
            instance.Bindings.TryForm(token) is { Signature: not "GLOB" } && !instance.Bindings.HasVariable(token);
        // Legacy errors do not identify their block. Refuse an ambiguous
        // MenuMode site rather than selecting one from current menu state.
        if (instance.PendingCommand is null && instance.MenuProgram.MissingCommandContinuation(error, Fixed) is not null) return;
        var continuation = instance.Program.MissingCommandContinuation(error, Fixed, instance.PendingCommand?.Statement);
        if (continuation is null) return;
        var invocation = checked(instance.Clock.Invocations + 1);
        var legacy = instance.PendingCommand is null;
        try
        {
            Execute(instance, host, continuation);
            ++instance.Executions;
            instance.Clock.CompleteInvocation();
            instance.Continuations.Add(new(error, ScriptHash(instance), continuation.StartStatement, invocation,
                "tail-completed-prefix-retained", legacy));
            instance.Error = null;
            instance.PendingCommand = null;
            instance.ContinuationError = null;
            _unbound.Remove(instance.Quest.FormKey);
        }
        catch (Exception failure) when (failure is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            instance.ContinuationError = failure.Message;
            if (continuation.LastStatement > continuation.StartStatement)
            {
                instance.Continuations.Add(new(error, ScriptHash(instance), continuation.StartStatement, invocation,
                    "tail-stopped-prefix-retained", legacy));
                RetainFailure(instance, failure, continuation, true);
            }
            // Failure at the reconstructed command keeps its old cursor and
            // error. No suffix ran, and no failed invocation was completed.
        }
    }
}
