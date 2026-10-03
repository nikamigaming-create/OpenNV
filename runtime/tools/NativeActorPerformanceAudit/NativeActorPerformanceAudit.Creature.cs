using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private void ExerciseCreatureAssembly(string game, string mod, string root, string[] references, string[] dependencies)
    {
        var installation = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        using var content = installation.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var globals = FalloutGlobalState.Read(records);
        var quests = new FalloutQuestState(records);
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        foreach (var identity in references)
        {
            var parts = identity.Split(':');
            if (parts.Length != 2) throw new ArgumentException("Creature audit requires owner:FormID reference identities.");
            var key = new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16));
            var source = records.GetEffective(key);
            if (source.Signature != "ACRE") throw new ArgumentException($"{key} is not an owned creature reference.");
            var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(source)!.Value);
            world.LoadCell(cell);
            var reference = cell.References.Single(value => value.FormKey == key);
            world.InitializeActorTemplates(key, 1, globals);
            var state = world.Get(key);
            var appearance = FalloutCreatureAppearanceResolver.Resolve(records, reference.Base, key, state.Templates);
            var hashes = appearance.Models.Prepend(appearance.SkeletonPath).ToDictionary(path => path, Hash);
            var sourceHash = SHA256.HashData(source.ReadData());
            var actor = RuntimeNativeCreature.Create(records, content, reference, state, units);
            try
            {
                AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                actor.ConfigureAi(records, quests, world, globals: globals);
                actor.EvaluatePackages(true);
                if (actor.Error is not null || actor.Parts.Count != appearance.Models.Count ||
                    actor.Parts.Sum(part => part.Surfaces) == 0 || state.QueryCurrentPackage is null)
                    throw new InvalidDataException("Owned creature assembly lost source geometry or its package query owner.");
                GD.Print($"OPENNV_CREATURE_ASSEMBLY_PASS reference={key} parts={actor.Parts.Count} surfaces={actor.Parts.Sum(part => part.Surfaces)} " +
                    "ownedMaterials=true packageOwnerRegistered=true packageExecution=unverified finalPixels=unverified");
            }
            finally { actor.Free(); }
            if (state.QueryCurrentPackage is not null || !sourceHash.SequenceEqual(SHA256.HashData(source.ReadData())) ||
                hashes.Any(pair => pair.Value != Hash(pair.Key)))
                throw new InvalidDataException("Creature audit retained a retired package owner or changed an owned source input.");
            GD.Print($"OPENNV_CREATURE_ASSEMBLY_RETIREMENT_PASS reference={key} sourceReadonly=true ownerRetired=true");
        }

        string Hash(string path) => content.TryRead(path, null, out var bytes, out _)
            ? Convert.ToHexString(SHA256.HashData(bytes)) : throw new FileNotFoundException(path);
    }
}
