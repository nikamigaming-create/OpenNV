using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedSleepProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutFormKey("FalloutNV.esm", 0x104e85);
        var package = records.GetEffective(new("FalloutNV.esm", 0x176ad3));
        var source = FalloutSleepPackage.Read(package);
        var scope = source.Scope(world, actor);
        var cell = FalloutCellSceneReader.Read(records, scope.Cell); world.LoadCell(cell);
        var bed = source.Candidates(records, world, actor).Single();
        var progress = new FalloutActorSleepProgress(scope.Cell, bed.FormKey, false);
        source.Validate(records, world, actor, progress);
        var furniture = records.GetEffective(bed.Base);
        var retained = new[] { package, records.GetEffective(actor), records.GetEffective(bed.FormKey), furniture }
            .ToDictionary(record => record.FormKey, record => SHA256.HashData(record.ReadData()));
        var model = cell.BaseObjects[bed.Base].ModelPath ?? throw new InvalidDataException("Owned Sleep bed has no source model.");
        if (!content.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
        var nif = FalloutNifFile.Read(bytes);
        var seats = FalloutFurnitureSource.ReadSeats(records, furniture, nif);
        if (source.Location.Type != 1 || source.Location.Radius != 0 || seats.Count == 0 || seats.Any(seat => !seat.Sleeping) ||
            !world.ReserveFurnitureSeat(bed.FormKey, seats[0].Index, actor))
            throw new InvalidDataException("Owned Sleep lost its in-cell scope, real bed marker or exclusive reservation.");
        var goal = FalloutPackageTravelGoal.Read(records, world, actor, package, progress);
        var leg = goal.Start(records, world, actor);
        if (leg.Complete || leg.Cell != scope.Cell || !leg.Location.SequenceEqual(world.Placement(bed.FormKey).Position))
            throw new InvalidDataException("Owned Sleep destination completed the parent package or lost the source bed.");
        var visited = new HashSet<FalloutFormKey>();
        var portals = new List<string>();
        // The editor placement may need an exit and a later entry. Each XTEL
        // is an intermediate source transition, never final bed arrival.
        while (!world.SameTravelRegion(world.Placement(actor).Cell, leg.Cell))
        {
            if (!visited.Add(world.TravelRegion(world.Placement(actor).Cell)))
                throw new InvalidDataException("Owned Sleep source portal route repeated a region.");
            var door = world.TravelPortal(actor, leg.Cell) ?? throw new InvalidDataException("Owned Sleep has no next admitted source portal.");
            world.ValidateTravelPortalLeg(actor, door, leg.Cell);
            var arrival = world.TravelPortalArrival(door, out var destinationDoor);
            world.SetPlacement(actor, arrival);
            world.ValidateTravelPortalArrival(actor, leg with { PortalArrival = new(door, destinationDoor) });
            portals.Add($"{door}>{destinationDoor}");
        }
        if (portals.Count == 0 || world.Placement(actor).Cell != leg.Cell)
            throw new InvalidDataException("Owned Sleep source portal chain did not reach its declared interior CELL.");
        if (retained.Any(pair => !pair.Value.SequenceEqual(SHA256.HashData(records.GetEffective(pair.Key).ReadData()))))
            throw new InvalidDataException("Owned Sleep audit changed its source records.");
        Console.WriteLine($"OPENNV_OWNED_SLEEP_PASS actor={actor} package={source.Form} cell={scope.Cell} bed={bed.FormKey} " +
            $"seats={seats.Count} model={identity} modelSha256={nif.Sha256} portals={string.Join(',', portals)} " +
            "sourceLocation=true access=true reservations=true parentComplete=false sourceReadonly=true nativeArrivalAndWake=separate");
    }
}
