using OpenNV.Runtime.Content;

internal static class ScriptContinuationContracts
{
    internal static void Run()
    {
        const string error = "Reached native script command Emitter.PlaySound3D (1 arguments) has no owner.";
        var source = FalloutGameModeProgram.Read("begin GameMode\nset prefix to prefix + 1\n" +
            "if GetGameLoaded\nif gate == 1\nEmitter.PlaySound3D Sound\nset suffix to suffix + 1\n" +
            "elseif GetRandomPercent\nset wrong to 1\nelse\nset wrong to 2\nendif\n" +
            "else\nset wrong to 3\nendif\nset done to 1\nend");
        var values = new Dictionary<string, double> { ["prefix"] = 0, ["suffix"] = 0, ["gate"] = 1, ["wrong"] = 0, ["done"] = 0 };
        var lifecycleReads = 0; var sounds = 0;
        FalloutScriptFunction? Functions(string name) => name.ToLowerInvariant() switch
        {
            "getgameloaded" => new([], _ => ++lifecycleReads == 1 ? 1 : throw new InvalidDataException("Consumed lifecycle guard repeated.")),
            "getrandompercent" => new([], _ => throw new InvalidDataException("Unselected random guard ran.")),
            _ => null,
        };
        try { source.Execute(name => values[name], (name, value) => values[name] = value, (_, _) => throw new NotSupportedException(error), Functions); }
        catch (NotSupportedException failure) when (failure.Message == error) { }
        Require(values["prefix"] == 1 && values["suffix"] == 0 && lifecycleReads == 1, "Missing command did not retain its actual prefix.");
        values["gate"] = 0;
        bool Fixed(string token) => token is "Emitter" or "Sound";
        var continued = source.MissingCommandContinuation(error, Fixed) ?? throw new InvalidDataException("Unique fixed command cannot continue.");
        continued.Execute(name => values[name], (name, value) => values[name] = value, (command, arguments) =>
        {
            Require(command == "Emitter.PlaySound3D" && arguments.SequenceEqual(["Sound"]), "Continuation changed the stopped command.");
            ++sounds;
        }, Functions);
        Require(values["prefix"] == 1 && values["suffix"] == 1 && values["done"] == 1 && values["wrong"] == 0 &&
            lifecycleReads == 1 && sounds == 1, "Continuation replayed a prefix/guard or selected a different branch.");
        var repeated = FalloutGameModeProgram.Read("begin GameMode\nEmitter.PlaySound3D Sound\nEmitter.PlaySound3D Sound\nend");
        Require(repeated.MissingCommandContinuation(error, Fixed) is null &&
            repeated.MissingCommandContinuation(error, Fixed, 1)?.StartStatement == 1 &&
            repeated.MissingCommandContinuation(error, Fixed, 9) is null, "Legacy ambiguous command or invalid captured cursor was admitted.");
        Require(FalloutGameModeProgram.Read("begin GameMode\nwhile gate\nEmitter.PlaySound3D Sound\nloop\nend")
            .MissingCommandContinuation(error, Fixed) is null, "An unsaved loop frame was reconstructed.");
        Require(FalloutGameModeProgram.Read("begin GameMode\nEmitter.PlaySound3D mutableSound\nend")
            .MissingCommandContinuation(error, Fixed) is null &&
            FalloutGameModeProgram.Read("begin GameMode\nmutableCaller.PlaySound3D Sound\nend")
                .MissingCommandContinuation(error.Replace("Emitter", "mutableCaller"), Fixed) is null,
            "Continuation admitted a mutable/prepared operand.");
        var alternate = FalloutGameModeProgram.Read("begin GameMode\nif gate == 0\nset wrong to 1\n" +
            "elseif gate == 1\nif GetRandomPercent\nset wrong to 2\nelse\nEmitter.PlaySound3D Sound\n" +
            "set suffix to suffix + 1\nendif\nelse\nset wrong to 3\nendif\nset done to 1\nend");
        var alternateTail = alternate.MissingCommandContinuation(error, Fixed)!;
        alternateTail.Execute(name => values[name], (name, value) => values[name] = value, (_, _) => ++sounds, Functions);
        Require(values["suffix"] == 2 && values["wrong"] == 0 && sounds == 2,
            "Else/elseif nesting lost the entered branch or repeated its guard.");
        Console.WriteLine("OPENNV_SCRIPT_CONTINUATION_CONTRACT_PASS consumedPrefix=true consumedGuards=true selectedBranches=true fixedOperands=true capturedCursor=true ambiguousLegacyAndLoopsRejected=true");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
