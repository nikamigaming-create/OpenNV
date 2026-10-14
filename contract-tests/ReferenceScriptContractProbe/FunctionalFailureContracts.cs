using OpenNV.Runtime.Content;

internal static class FunctionalFailureContracts
{
    internal static void Run()
    {
        foreach (var failed in new[] { false, true })
        {
            using var fixture = new ScriptSaveFixture("", "set suffix to 5");
            var quests = new FalloutQuestState(fixture.Records);
            var scripts = new FalloutQuestScripts(fixture.Records, quests, new HashSet<FalloutFormKey>(), new(),
                references: fixture.World, defaultProcessingDelay: 0);
            if (failed)
            {
                fixture.World.RetainFunctionalFailure(new InvalidOperationException("first actual Main failure"), "authored-Main");
                fixture.World.RetainFunctionalFailure(new InvalidDataException("later failure"), "authored-later-child");
                ScriptManualSaveContracts.Require(fixture.World.FunctionalFailure is
                { Owner: "authored-Main", Error: "first actual Main failure" }, "Gameplay fault replaced its actual first failure.");
                try { _ = fixture.World.Capture(); throw new Exception("Faulted World admitted a gameplay save."); }
                catch (InvalidOperationException) { }
            }
            scripts.Advance(0);
            ScriptManualSaveContracts.Require(quests.Variable(fixture.Quest, 2) == (failed ? 0 : 5),
                "Quest mutation continued after Main failed, or healthy gameplay was incorrectly inhibited.");
        }
        Console.WriteLine("OPENNV_FUNCTIONAL_FAILURE_PASS sharedWorld=true firstFailureRetained=true questMutationStopped=true healthyQuestRuns=true saveRefused=true quit=UNEXECUTED");
    }
}
