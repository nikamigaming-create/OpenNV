using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private readonly Dictionary<(FalloutFormKey Caller, string Scope), string> _closedResultFailures = [];

    private FalloutPluginRecord CompiledBindingOwner(FalloutFormKey caller, FalloutCompiledScriptProgram program) =>
        !program.Standalone && records.RuntimeFormId(caller) == 0x14
            ? program.Source : records.GetEffective(caller);

    internal void ExecutePlayerPackageEvent(FalloutPackageEvent program, Action<FalloutScriptResultReceipt> committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        program.ValidateScript();
        var player = records.RuntimeFormKey(0x14);
        // The engine player is the caller. The original PACK provides embedded
        // reference bindings; it never stands in for a placed actor or locals.
        var receipt = ExecuteScopeOwned(program.Scope, player, program.Package);
        committed(receipt);
        if (program.Topic is { } topic)
        {
            if (records.GetEffective(topic).Signature != "DIAL")
                throw new InvalidDataException("Player package event topic is not DIAL.");
            host.Apply(new(FalloutReferenceEffectKind.PackageEventTopic, player, player,
                program.Package.FormKey, Topic: topic, PackageEvent: program.Kind));
        }
    }

    internal FalloutScriptResultReceipt ExecuteResultOwned(FalloutDialogueInfo info, FalloutFormKey caller, bool begin)
    {
        var scope = FalloutScriptScope.Dialogue(info.Record, begin);
        return ExecuteScopeOwned(scope, caller, records.GetEffective(info.Quest));
    }

    private FalloutScriptResultReceipt ExecuteScopeOwned(FalloutScriptScope scope, FalloutFormKey caller,
        FalloutPluginRecord diagnosticOwner, Action<FalloutGameModeProgram, Exception>? sourceFailure = null)
    {
        scope.RequireSource(records.GetEffective(scope.Source.FormKey));
        if (_closedResultFailures.TryGetValue((caller, scope.ScopeSha256), out var closed))
            throw new NotSupportedException(closed);
        if (!FalloutScriptResultReceipt.HasProgram(scope)) return FalloutScriptResultReceipt.CompleteAbsent(scope, caller);
        var steps = 0;
        ulong invocation = 0;
        FalloutGameModeProgram? diagnostic = null;
        try
        {
            var authority = scope.Compiled ? FalloutScriptResultAuthority.CompiledVanilla : FalloutScriptResultAuthority.SourceDiagnostic;
            string hash;
            IEnumerable<bool> execution;
            if (scope.Compiled)
            {
                var compiled = FalloutCompiledScriptProgram.Read(scope.Source, scope, standalone: false);
                hash = compiled.ProgramSha256;
                execution = CompiledSteps(caller, compiled, observeInvocation: entered => invocation = entered.Invocation);
            }
            else
            {
                var source = FalloutGameModeProgram.Read("begin Result\n" + scope.DiagnosticSource() + "\nend", "Result");
                diagnostic = source;
                hash = source.ProgramSha256;
                execution = Steps(caller, Bindings(diagnosticOwner, scope.Source, scope), source, null, 0,
                    observeInvocation: entered => invocation = entered.Invocation, executionScope: scope.ScopeSha256);
            }
            foreach (var _ in execution) steps = checked(steps + 1);
            if (invocation == 0) throw new InvalidOperationException("Completed result has no shared invocation lease.");
            var receipt = FalloutScriptResultReceipt.Capture(scope, caller, authority, hash, invocation, steps);
            receipt.Require(scope, caller);
            return receipt;
        }
        catch (Exception error)
        {
            if (diagnostic is not null) sourceFailure?.Invoke(diagnostic, error);
            if (scope.Compiled)
            {
                // Earlier effects remain committed; the failed instruction and
                // the suffix do not run. Repeat input cannot replay the prefix.
                _closedResultFailures[(caller, scope.ScopeSha256)] = error.Message;
                world.ScriptManualSaves.RetainCompiledResultFailure(scope, caller, steps, error);
            }
            throw;
        }
    }
}
