using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private static void NativeCreaturePackageSitting(RuntimeNativeNpc npc, RuntimeNativeCreature creature,
        FalloutPluginStack records, FalloutReferenceWorld world, FalloutFormKey npcReference, FalloutPluginRecord package)
    {
        var names = package.Plugin.Masters.Append(package.Plugin.Name).ToArray();
        var index = Array.FindIndex(names, name => string.Equals(name, npcReference.OwnerPlugin, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidDataException("Native creature sitting subject has no declaring source namespace.");
        var condition = new FalloutCondition(package, 0, npc.SittingState, 159, 0, 0, 2,
            checked((uint)index << 24) | npcReference.ObjectId);
        if (creature.Appearance.Reference == npcReference ||
            creature.PackageCondition(condition) != world.GetSitting(npcReference))
            throw new InvalidDataException("Creature sitting query did not reach the other actual native NPC's physical owner.");
        var physical = world.Get(npcReference).QuerySitting; var retained = world.Get(npcReference).FurnitureContinuation;
        try
        {
            world.Get(npcReference).QuerySitting = null; world.Get(npcReference).FurnitureContinuation = null;
            var refused = false;
            try { _ = creature.PackageCondition(condition); }
            catch (NotSupportedException) { refused = true; }
            if (!refused) throw new InvalidDataException("Creature sitting query borrowed its caller after the explicit NPC owner disappeared.");
        }
        finally { world.Get(npcReference).QuerySitting = physical; world.Get(npcReference).FurnitureContinuation = retained; }
        GD.Print($"OPENNV_NATIVE_CREATURE_PACKAGE_SITTING_PASS creature={creature.Appearance.Reference} subject={npcReference} " +
            $"phase={npc.SittingState} actualOtherNativeOwner=true missingOwnerRefused=true adapterInput=neutral-component gameplayAndParity=unverified recording=false");
    }

    private static void NativePackageSitting(RuntimeNativeNpc warm, RuntimeNativeNpc resumed,
        FalloutPluginStack records, FalloutReferenceWorld world, FalloutReferenceWorld cold,
        FalloutFormKey actor, FalloutActorFurnitureContinuation continuation)
    {
        if (warm.SittingState != 3 || resumed.SittingState != 3 || world.GetSitting(actor) != 3 || cold.GetSitting(actor) != 3)
            throw new InvalidDataException("Native sitting condition fixture did not retain actual warm/cold occupation.");
        var package = records.GetEffective(continuation.Assignment.Package);
        uint DeclaredReference(FalloutFormKey key)
        {
            var names = package.Plugin.Masters.Append(package.Plugin.Name).ToArray();
            var index = Array.FindIndex(names, name => string.Equals(name, key.OwnerPlugin, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidDataException("Native sitting query subject is outside its declaring source namespaces.");
            return checked((uint)index << 24) | key.ObjectId;
        }
        // These neutral adapter inputs borrow only the declaration scope. The
        // measured phase comes from actual native occupied/cold furniture.
        var self = new FalloutCondition(package, 0, 3, 159, 0, 0, 0, 0);
        var explicitActor = self with { RunOn = 2, Reference = DeclaredReference(actor) };
        var beforeWarm = JsonSerializer.Serialize(world.Capture()); var beforeCold = JsonSerializer.Serialize(cold.Capture());
        if (warm.EvaluateAiCondition(self) != 3 || warm.EvaluateAiCondition(explicitActor) != 3 ||
            resumed.EvaluateAiCondition(self) != 3 || resumed.EvaluateAiCondition(explicitActor) != 3)
            throw new InvalidDataException("Native AI sitting dispatch lost its actual physical warm/cold owner.");
        foreach (var invalid in new[] { explicitActor with { Argument1 = 1 }, explicitActor with { Argument2 = 1 },
            explicitActor with { RunOn = 3 }, explicitActor with { Reference = 0 },
            explicitActor with { Reference = DeclaredReference(continuation.Furniture!.Value) } })
        {
            static void Refuse(Func<float> query)
            {
                try { _ = query(); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
                throw new InvalidDataException("Native sitting query admitted invalid arguments, scope or a furniture subject.");
            }
            Refuse(() => warm.EvaluateAiCondition(invalid)); Refuse(() => resumed.EvaluateAiCondition(invalid));
        }
        var physical = cold.Get(actor).QuerySitting; var retained = cold.Get(actor).FurnitureContinuation;
        try
        {
            cold.Get(actor).QuerySitting = null; cold.Get(actor).FurnitureContinuation = null;
            var refused = false;
            try { _ = resumed.EvaluateAiCondition(explicitActor); }
            catch (NotSupportedException) { refused = true; }
            if (!refused) throw new InvalidDataException("Native sitting query substituted its caller after explicit physical-owner loss.");
        }
        finally { cold.Get(actor).QuerySitting = physical; cold.Get(actor).FurnitureContinuation = retained; }
        if (JsonSerializer.Serialize(world.Capture()) != beforeWarm || JsonSerializer.Serialize(cold.Capture()) != beforeCold)
            throw new InvalidDataException("Sitting query or refusal mutated the native warm/cold continuation.");
        GD.Print($"OPENNV_NATIVE_PACKAGE_SITTING_PASS actor={actor} package={package.FormKey} occupied=true " +
            "nativeNpcDispatcher=true nativeCold=true selfAndExplicit=true missingPhysicalOwnerRefused=true " +
            "invalidArgumentsScopeAndFurnitureRefused=true stateUnchanged=true adapterInput=neutral-component gameplayAndParity=unverified recording=false");
    }
}
