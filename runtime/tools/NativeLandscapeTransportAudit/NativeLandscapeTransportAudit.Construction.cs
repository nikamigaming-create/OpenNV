using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeLandscapeTransportAudit
{
    private async Task RunConstructionLifetimeAsync(string[] args)
    {
        if (args.Length is not (3 or 5 or 7) || args[0] != "--construction-lifetime" ||
            args.Length >= 5 && args[3] != "--mod-stack" || args.Length == 7 && args[5] != "--mod-order")
            throw new ArgumentException("Expected --construction-lifetime owned-root source-interior [--mod-stack existing-launcher-list-path [--mod-order automatic|manual]].");
        var installation = NativeGameInstallation.Detect(args[1]);
        var campaign = installation.Game switch
        {
            NativeGame.Fallout3 => RuntimeLiveContentSource.Fallout3Game,
            NativeGame.FalloutNewVegas => RuntimeLiveContentSource.FalloutNewVegasGame,
            _ => throw new NotSupportedException("LAND construction requires a detected owned Fallout 3/New Vegas installation.")
        };
        if (args.Length >= 5)
        {
            var options = new Dictionary<string, string>
            {
                ["mod-stack"] = File.ReadAllText(args[4]), ["mod-order"] = args.Length == 7 ? args[6] : "automatic"
            };
            var stack = FalloutModStackSelection.ReadOptions(options)!.Resolve(args[1]);
            RuntimeLiveContentSource.Configure(args[1], campaign, stack.ContentRoots.Skip(1).ToArray(), stack.ActivePlugins, stack.Settings);
        }
        else RuntimeLiveContentSource.Configure(args[1], campaign);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var interior = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", args[2]).FormKey);
        var exit = FalloutDoorTransitionResolver.ResolveInteriorExits(records, interior).Single();
        var entry = exit.SourceDoor.Teleport!.Position;
        var gridSource = new FalloutExteriorGrid(records);
        var diameter = checked((int)FalloutInstallationSettings.Read(content).Unsigned("General", "uGridsToLoad"));
        var grid = gridSource.Resolve(exit.DestinationWorldspace, gridSource.PersistentCell(exit.DestinationWorldspace), entry[0], entry[1], diameter);
        var source = FalloutLandscapeTransportResolver.ResolveCell(records, grid.Cells.Single(cell => cell.FormKey == grid.Scene.Cell.FormKey));
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        var cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        var created = new List<RuntimeNativeLandscapeConstruction>();
        var foreign = new Node3D { Name = "AuthoredForeignTerrainBorrower" }; AddChild(foreign);
        var successful = new List<RuntimeNativeLandscapeTransport>();
        string? completed = null;
        try
        {
            var phases = new[] { RuntimeNativeLandscapeConstructionPhase.Mesh, RuntimeNativeLandscapeConstructionPhase.Material,
                RuntimeNativeLandscapeConstructionPhase.Geometry, RuntimeNativeLandscapeConstructionPhase.Root,
                RuntimeNativeLandscapeConstructionPhase.Collision, RuntimeNativeLandscapeConstructionPhase.Returned };
            foreach (var phase in phases)
            {
                RuntimeNativeLandscapeConstruction? observed = null;
                var original = new InvalidDataException("Authored actual LAND constructor interruption at " + phase);
                Exception? returned = null;
                try
                {
                    _ = RuntimeNativeLandscapeTransportBuilder.Build(source, units, cache, construction =>
                    {
                        observed = construction;
                        if (construction.Phase == RuntimeNativeLandscapeConstructionPhase.Entered) created.Add(construction);
                        if (construction.Phase == phase) throw original;
                    });
                }
                catch (Exception error) { returned = error; }
                Require(observed is not null && ReferenceEquals(returned, original) && ReferenceEquals(observed.OriginalFailure, original) &&
                    observed.Phase == RuntimeNativeLandscapeConstructionPhase.Retired && observed.NativeNodesDestroyed &&
                    observed.ResourceIdentities.All(identity => !GodotObject.IsInstanceIdValid(identity)),
                    "Actual interrupted LAND construction lost its source error or native allocations.");
                Require(cache.Values.All(GodotObject.IsInstanceValid), "Failed private LAND retirement disposed another cache owner's decoded texture.");
            }

            RuntimeNativeLandscapeConstruction? failedParent = null;
            Node? borrowedNode = null;
            var parentOriginal = new InvalidDataException("Authored actual LAND allocation changed parent");
            try
            {
                _ = RuntimeNativeLandscapeTransportBuilder.Build(source, units, cache, construction =>
                {
                    if (construction.Phase == RuntimeNativeLandscapeConstructionPhase.Entered) { created.Add(construction); failedParent = construction; }
                    if (construction.Phase != RuntimeNativeLandscapeConstructionPhase.Geometry) return;
                    borrowedNode = GodotObject.InstanceFromId(construction.NodeIdentities.Single()) as Node ??
                        throw new InvalidDataException("The actual LAND allocation has no living native Node binding.");
                    foreign.AddChild(borrowedNode); throw parentOriginal;
                });
                throw new InvalidOperationException("A foreign native parent was silently accepted.");
            }
            catch (AggregateException)
            {
                Require(failedParent is not null && ReferenceEquals(failedParent.OriginalFailure, parentOriginal) &&
                    failedParent.Phase == RuntimeNativeLandscapeConstructionPhase.Retiring && borrowedNode is not null &&
                    GodotObject.IsInstanceValid(borrowedNode) && borrowedNode.GetParent() == foreign && failedParent.RetirementErrors.Count != 0,
                    "Foreign native ownership was destroyed or falsely marked retired.");
            }
            var failedConstruction = failedParent ?? throw new InvalidDataException("Foreign-parent refusal lost its actual construction owner.");
            var failedNode = borrowedNode ?? throw new InvalidDataException("Foreign-parent refusal lost its actual native allocation.");
            foreign.RemoveChild(failedNode); failedConstruction.RetireFailedAllocations();
            Require(failedConstruction.Phase == RuntimeNativeLandscapeConstructionPhase.Retired && ReferenceEquals(failedConstruction.OriginalFailure, parentOriginal),
                "Retry erased the original failed constructor or skipped actual reclaimed allocation retirement.");

            var first = RuntimeNativeLandscapeTransportBuilder.Build(source, units, cache,
                construction => { if (construction.Phase == RuntimeNativeLandscapeConstructionPhase.Entered) created.Add(construction); });
            successful.Add(first); AddChild(first); first.Construction.BindActualParent(this);
            var externalMesh = new MeshInstance3D { Mesh = first.Geometry.Mesh }; foreign.AddChild(externalMesh);
            var firstIdentity = first.GetInstanceId(); first.Free();
            Reject(() => first.Construction.ObserveNativeDestructionAndRetireResources());
            Require(!GodotObject.IsInstanceIdValid(firstIdentity) && GodotObject.IsInstanceValid(externalMesh) &&
                first.Construction.Phase == RuntimeNativeLandscapeConstructionPhase.Retiring,
                "A live external native mesh borrower was destroyed or reported as complete retirement.");
            externalMesh.Free(); first.Construction.ObserveNativeDestructionAndRetireResources();
            Require(first.Construction.Phase == RuntimeNativeLandscapeConstructionPhase.Retired,
                "Actual native borrower release did not permit retained private resource retirement.");

            var observedInvalid = 0;
            Reject(() => RuntimeNativeLandscapeTransportBuilder.Build(source with { Heights = source.Heights[..^1] }, units, cache,
                _ => observedInvalid++));
            Require(observedInvalid == 0, "Malformed source extent entered native allocation before admission.");
            var second = RuntimeNativeLandscapeTransportBuilder.Build(source, units, cache,
                construction => { if (construction.Phase == RuntimeNativeLandscapeConstructionPhase.Entered) created.Add(construction); });
            successful.Add(second); AddChild(second); second.Construction.BindActualParent(this);
            Require(second.GetInstanceId() != firstIdentity && second.Construction.Identity != first.Construction.Identity &&
                second.Construction.TransportSha256 == first.Construction.TransportSha256,
                "A fresh source constructor reused its retired actual native lifetime.");
            second.Free(); second.Construction.ObserveNativeDestructionAndRetireResources();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(created.All(construction => construction.Phase == RuntimeNativeLandscapeConstructionPhase.Retired),
                "Construction audit still owns an entered native allocation.");
            completed = $"OPENNV_NATIVE_LAND_CONSTRUCTION_PASS stack={content.StackId} cell={source.ActiveCell} land={source.Landscape} " +
                $"transport={FalloutCellNativeSource.TransportDigest(source)} interrupted={phases.Length} actualNativeNodes=true " +
                "originalFailures=true foreignParentRefused=true resourceBorrowerRefused=true exactRetry=true malformedBeforeAllocation=true " +
                "sharedTextureBorrowPreserved=true newNativeLifetime=true pixels=unverified gpuFence=unverified parity=unmeasured";
        }
        finally
        {
            var errors = new List<Exception>();
            foreach (var land in successful.Where(GodotObject.IsInstanceValid))
                try { land.Free(); } catch (Exception error) { errors.Add(error); }
            if (GodotObject.IsInstanceValid(foreign))
                try { foreign.Free(); } catch (Exception error) { errors.Add(error); }
            foreach (var construction in created)
                try
                {
                    if (construction.OriginalFailure is not null) construction.RetireFailedAllocations();
                    else construction.ObserveNativeDestructionAndRetireResources();
                }
                catch (Exception error) { errors.Add(error); }
            foreach (var texture in cache.Values.Distinct())
                try { texture.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Native LAND construction audit retains actual allocation retirement errors.", errors);
        }
        GD.Print(completed ?? throw new InvalidDataException("LAND construction audit did not complete its actual selected assertions."));
    }
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException or AggregateException) { return; }
        throw new InvalidOperationException("An unfinished/foreign actual LAND allocation was admitted.");
    }
}
