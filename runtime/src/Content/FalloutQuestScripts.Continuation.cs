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
        var error = instance.Error!;
        if (error == $"No record exists for {_records.RuntimeFormKey(0x14)}." && instance.PendingCommand is null &&
            !instance.Continuations.Any(receipt => receipt.Error == error && receipt.Statement == 0 && receipt.LegacySourceUnverified) &&
            host?.CanRetryPlayerDeathRead?.Invoke(instance.Quest, instance.Script, instance.Program) == true)
        {
            ContinueInitialPlayerDeathRead(instance, host, error);
            return;
        }
        // Admission is capability-specific, never quest/location-specific.
        // Non-missing failures and unowned operations remain latched. The
        // newly bound command still validates its source operands. Actor-flag
        // repair requires an exact saved instruction; it never replays the
        // script prefix or guesses an instruction from an older error string.
        var soundReady = Sounds.IsBound && Regex.IsMatch(error,
            @"^Reached (?:native script|object-script) command (?:\w+\.)?playSound3D \(1 arguments\) has no owner\.$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var actorFlagsReady = References is not null && instance.PendingCommand is { GameMode: true } &&
            Regex.IsMatch(error, @"^Reached (?:native script|object-script) command SetEssential \(2 arguments\) has no owner\.$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if ((!soundReady && !actorFlagsReady) || instance.PendingCommand is { GameMode: false }) return;
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

    private void ContinueInitialPlayerDeathRead(Instance instance, FalloutQuestScriptHost host, string error)
    {
        // The old GetDead implementation always failed on the engine-created
        // player. Source inspection proves this was the first reached read,
        // before any effect; ambiguous MenuMode failures stay latched. The
        // persisted receipt also latches a newly reached suffix failure that
        // happens to have the same generic error as the old first read.
        var invocation = checked(instance.Clock.Invocations + 1);
        try
        {
            Execute(instance, host);
            ++instance.Executions;
            instance.Clock.CompleteInvocation();
            instance.Continuations.Add(new(error, ScriptHash(instance), 0, invocation,
                "tail-completed-prefix-retained", true));
            instance.Error = null;
            instance.ContinuationError = null;
            _unbound.Remove(instance.Quest.FormKey);
        }
        catch (Exception failure) when (failure is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            instance.Continuations.Add(new(error, ScriptHash(instance), 0, invocation,
                "tail-stopped-prefix-retained", true));
            RetainFailure(instance, failure, instance.Program, true);
        }
    }
}
