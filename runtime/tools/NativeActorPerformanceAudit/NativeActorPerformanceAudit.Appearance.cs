using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void ExerciseAppearances(string root, string[] references)
    {
        if (references.Length == 0) throw new ArgumentException("Appearance audit requires placed actor identities.");
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var globals = FalloutGlobalState.Read(records);
        foreach (var hex in references)
        {
            var key = records.RuntimeFormKey(Convert.ToUInt32(hex, 16));
            var source = records.GetEffective(key);
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(source)!.Value);
            var reference = cell.References.Single(item => item.FormKey == key);
            var selection = world.InitializeActorTemplates(key, 1, globals);
            var armor = world.EquippedArmor(key, 1, globals);
            var started = Stopwatch.GetTimestamp();
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, reference.Base, key, armor, selection: selection);
            var sceneThread = System.Environment.CurrentManagedThreadId;
            var prepared = FalloutContentWorkers.Run(() =>
            {
                if (System.Environment.CurrentManagedThreadId == sceneThread) throw new InvalidOperationException("NPC source work ran on the scene thread.");
                return FalloutNpcPreparedGeometry.Read(appearance, content);
            }).GetAwaiter().GetResult();
            var prepareMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            VerifyPreparedNpc(prepared, content);
            var phases = new List<double>();
            using var assembly = new RuntimeNativeNpc.Assembly(prepared, content, .0142875f,
                (look, part, nif, geometry) => NativeNpcMaterial.Resolve(look, part, nif, geometry, records, new Color(.3f, .3f, .3f)));
            var complete = false;
            while (!complete)
            {
                started = Stopwatch.GetTimestamp();
                complete = assembly.Advance();
                phases.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            var actor = assembly.Take();
            actor.BindSourceBehavior(records, selection);
            GD.Print("OPENNV_NPC_ASSEMBLY_COST " + JsonSerializer.Serialize(new { key, prepareMilliseconds, nativeMilliseconds = phases.Sum(), maximumStepMilliseconds = phases.Max(), phases }));
            try
            {
                AddChild(actor);
                if (actor.Parts.Count != actor.Appearance.Models.Count ||
                    actor.Parts.Sum(part => part.Surfaces) == 0)
                    throw new InvalidDataException("Source NPC assembly omitted a selected body part.");
                GD.Print($"OPENNV_NPC_ASSEMBLY_PASS reference={key} parts={actor.Parts.Count} ownedMaterials=true runtimeInventory=true finalPixels=unverified");
            }
            finally { actor.Free(); }
        }
    }
}
