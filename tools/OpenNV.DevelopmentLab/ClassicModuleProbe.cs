using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

internal static class ClassicModuleProbe
{
    internal static int Run(string install, string campaign, string[] paths)
    {
        using IFalloutClassicOwnedSource source = campaign == "fallout-2"
            ? OpenNV.Runtime.Campaigns.Fallout2.Native.Fo2NativeOwnedSource.LoadInstall(install)
            : Fallout1OwnedContentSource.LoadInstall(install);
        var selected = paths.Length == 0 ? Fallout1NativeLists.Read(source.Read("scripts/scripts.lst", out _))
            .Select(name => ClassicMapCatalog.Canonical("scripts/" + Path.ChangeExtension(name, ".int"))).Distinct().ToArray() : paths;
        var passed = 0; var failures = 0;
        foreach (var path in selected)
        {
            try
            {
                var program = ClassicNativeIntReader.Read(source.Read(path, out _), path);
                var startup = program.Procedures[program.StartupProcedure!];
                // Isolate the real source initializer: the source startup event
                // requires gameplay owners and is explicitly outside this probe.
                var stop = new ClassicIntInstruction(startup.BodyOffset, 0x801c, null);
                var entry = startup with { CanonicalEpilogueOffset = null, Instructions = new[] { stop } };
                var procedures = program.Procedures.ToDictionary(row => row.Key, row => row.Value);
                procedures[startup.Name] = entry;
                var instructions = program.Instructions.ToDictionary(row => row.Key, row => row.Value);
                instructions[stop.Offset] = stop;
                var fixture = program with { Procedures = procedures, Instructions = instructions };
                var state = new ClassicIntProcedureState(new Dictionary<int, int>(), new Dictionary<int, int>(),
                    new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
                var context = new ClassicIntExpressionContext(state.ProgramVariables, state.LocalVariables, state.ScriptLocalVariables,
                    state.MapVariables, state.GlobalVariables, 0, 0, null, null, new Dictionary<(int, int), int>(),
                    new Dictionary<(int, int), int>(), new Dictionary<int, int>(), new Dictionary<(int, int), int>(),
                    new Dictionary<string, int>(), null, null, null, new ClassicIntObjectHandleTable(new Dictionary<ClassicIntObjectCreation, int>()));
                var result = ClassicIntProcedureVm.Execute(fixture, entry.Name, state, context, ClassicIntWorldObjectState.Empty, null, 100000, initializeModule: true);
                if (result.State.ValueStack.Count != 0 || result.ExternalOwners.Count != result.ExternalVariables.Count)
                    throw new InvalidOperationException("Initializer left an unbound stack or export owner.");
                if (campaign == "fallout-1" && path.Equals("scripts/v13cave.int", StringComparison.OrdinalIgnoreCase) &&
                    (result.State.ProgramVariables.Count != 6 || result.ExternalVariables.Count != 6 || result.ExternalVariables.Values.Any(value => value != 0)))
                    throw new InvalidOperationException("Owned opening initializer did not reproduce its six globals and six zero-valued exports.");
                passed++;
                Console.WriteLine($"PASS initializer-only {path} globals={result.State.ProgramVariables.Count} exports={result.ExternalVariables.Count}");
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or FileNotFoundException)
            { failures++; Console.WriteLine($"UNBOUND initializer-only {path}: {error.Message}"); }
        }
        Console.WriteLine($"OWNED_MODULE_INITIALIZERS campaign={campaign} passed={passed} unbound={failures} gameplay=false");
        return failures == 0 ? 0 : 1;
    }
}
