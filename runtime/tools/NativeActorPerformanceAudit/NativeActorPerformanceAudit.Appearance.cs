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
            var armor = world.EquippedArmor(key, 1, globals);
            var actor = RuntimeNativeNpc.Create(records, content, reference, .0142875f,
                (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.3f, .3f, .3f)), armor);
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
