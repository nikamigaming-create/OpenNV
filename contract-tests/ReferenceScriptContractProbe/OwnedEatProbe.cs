using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedEatProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutFormKey("FalloutNV.esm", 0x104c80);
        var package = records.GetEffective(new("FalloutNV.esm", 0x16a9b0));
        var source = FalloutEatPackage.Read(package);
        var scope = source.Scope(world, actor);
        var cell = FalloutCellSceneReader.Read(records, scope.Cell); world.LoadCell(cell);
        var chair = source.Location.Form ?? throw new InvalidDataException("Owned Eat lost its reference location.");
        var progress = new FalloutActorEatPackageProgress(scope.Cell, chair, true);
        source.Validate(records, world, actor, progress);
        var placed = world.PlacedSource(chair);
        var model = placed.BaseObject.ModelPath ?? throw new InvalidDataException("Owned Eat chair has no model.");
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var seats = FalloutFurnitureSource.ReadSeats(records, records.GetEffective(placed.Reference.Base), FalloutNifFile.Read(bytes));
        var foods = world.Inventory(actor, 1, null).Contents.Items.Count(item => item.Count > 0 && source.AcceptsFood(records, world, item.FormKey));
        if (source.Location.Type != 0 || source.Location.Radius != 0 || source.FoodTarget is not { Type: 2, RawTarget: 0, CountOrDistance: 0 } ||
            !source.ContainsChair(world, actor, chair) || seats.Count == 0 || seats.Any(seat => seat.Sleeping) || foods != 0)
            throw new InvalidDataException("Owned Eat lost its exact location, zero-radius/no-food input or real chair markers.");
        Console.WriteLine($"OPENNV_OWNED_EAT_PASS actor={actor} package={source.Form} chair={chair} seats={seats.Count} " +
            "sourceFoodCount=0 sourceRadius=0 sourceChairMarkers=true seatedNoFoodPolicy=true nativeHandoffAndPlayback=separate");
    }
}
