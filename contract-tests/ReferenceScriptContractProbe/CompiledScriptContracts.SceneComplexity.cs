using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    private static void SceneComplexityCommand(string directory)
    {
        StructuralOwnedFixture(directory, StructuralStandalone(Block(0, Join(
            Call(0x115d, Join(U16(1), Integer(-2))), Set('f', 1, " 7")))), records =>
        {
            using var world = new FalloutReferenceWorld(records); var quests = new FalloutQuestState(records);
            var session = new FalloutScriptSession();
            session.SetInCharGen(true);
            var effects = 0;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.SceneComplexity,
                    "Scene complexity was conflated with character generation or presentation.");
                session.SetSceneIsComplex(effect.Enable); ++effects;
            }));
            var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
            foreach (var _ in scripts.CompiledSteps(Key(0x20), program, program.Events[0])) { }
            Require(session.SceneIsComplex && session.InCharGen && effects == 1 && quests.Variable(Key(0x20), 1) == 7,
                "Compiled scene flag lost its signed integer writer or source suffix.");
            var cold = new FalloutScriptSession(); cold.Restore(Copy(session.Capture()));
            cold.SetSceneIsComplex(false);
            Require(!cold.SceneIsComplex && cold.InCharGen && session.SceneIsComplex && effects == 1,
                "Cold scene flag replayed an effect, changed the warm owner or aliased character generation.");
            foreach (var invalid in new[] { .5, double.NaN, double.PositiveInfinity, (double)int.MaxValue + 1 })
                Reject(() => FalloutScriptSession.SceneComplexityFlag(invalid));
        });
        Console.WriteLine("OPENNV_COMPILED_SCENE_COMPLEXITY_PASS actualWriter=true signedFlag=true independentChargen=true serializedCold=true");
    }
}
