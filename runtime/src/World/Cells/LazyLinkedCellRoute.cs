using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.Settings;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.World.Portals;
using OpenNV.Runtime.World.Streaming;

namespace OpenNV.Runtime.World.Cells;

internal sealed class LazyLinkedCellRoute
{
    private const int BitsPerByte = 8;
    private const int GodotPhysicsLayerCount = sizeof(uint) * BitsPerByte;
    private readonly Node3D _parent;
    private readonly GameplaySession _session;
    private readonly RuntimeConfiguration _configuration;
    private readonly string? _actorScenesManifestPath;
    private readonly bool _proofEnableActor;
    private readonly bool _buildCollision;
    private readonly bool _applyCellEnvironment;
    private readonly RuntimeMaterialLoader.TextureMemoryStore _textureMemory;
    private readonly IReadOnlyList<CellDescriptor> _descriptors;
    private readonly IReadOnlyList<RouteEdge> _edges;
    private readonly IReadOnlySet<string> _requiredReferenceFormIds;
    private readonly Dictionary<string, LoadedRouteCell> _loaded;
    private readonly List<CellSceneLoader.LinkedCell> _linkedCells;
    private readonly List<CellSceneLoader.PortalLink> _portalLinks;
    private readonly Dictionary<string, PickupInstance> _pickups;
    private readonly Dictionary<string, ContainerInstance> _containers;
    private readonly Dictionary<string, FurnitureInstance> _furniture;
    private readonly Dictionary<string, PoolTableInstance> _pools;
    private readonly List<CellActorLoader.PlacedActor> _actors;
    private readonly CellActiveSet _activeSet;
    private readonly CellEnvironmentSet? _environmentSet;
    private readonly GameplayActorGrounding _actorGrounding;
    private CellPortalTravel? _portalTravel;
    private readonly TaskCompletionSource _initialAdjacentReady = new();

    private LazyLinkedCellRoute(
        Node3D parent,
        GameplaySession session,
        RuntimeConfiguration configuration,
        string? actorScenesManifestPath,
        bool proofEnableActor,
        bool buildCollision,
        bool applyCellEnvironment,
        RuntimeMaterialLoader.TextureMemoryStore textureMemory,
        IReadOnlyList<CellDescriptor> descriptors,
        IReadOnlyList<RouteEdge> edges,
        IReadOnlySet<string> requiredReferenceFormIds,
        LoadedRouteCell initial,
        List<CellSceneLoader.LinkedCell> linkedCells,
        List<CellSceneLoader.PortalLink> portalLinks,
        Dictionary<string, PickupInstance> pickups,
        Dictionary<string, ContainerInstance> containers,
        Dictionary<string, FurnitureInstance> furniture,
        Dictionary<string, PoolTableInstance> pools,
        List<CellActorLoader.PlacedActor> actors,
        CellActiveSet activeSet,
        CellEnvironmentSet? environmentSet,
        GameplayActorGrounding actorGrounding)
    {
        _parent = parent;
        _session = session;
        _configuration = configuration;
        _actorScenesManifestPath = actorScenesManifestPath;
        _proofEnableActor = proofEnableActor;
        _buildCollision = buildCollision;
        _applyCellEnvironment = applyCellEnvironment;
        _textureMemory = textureMemory;
        _descriptors = descriptors;
        _edges = edges;
        _requiredReferenceFormIds = requiredReferenceFormIds;
        _loaded = new Dictionary<string, LoadedRouteCell>(StringComparer.OrdinalIgnoreCase)
        {
            [initial.Content.FormId] = initial,
        };
        _linkedCells = linkedCells;
        _portalLinks = portalLinks;
        _pickups = pickups;
        _containers = containers;
        _furniture = furniture;
        _pools = pools;
        _actors = actors;
        _activeSet = activeSet;
        _environmentSet = environmentSet;
        _actorGrounding = actorGrounding;
    }

    internal static CellSceneLoader.LoadedCell Load(
        string routeScenePath,
        JsonElement routeSource,
        Node3D parent,
        GameplaySession session,
        RuntimeConfiguration configuration,
        RuntimeSettingsState settings,
        OpeningGameplayVitalsContract? gameplayVitals,
        bool openProofDoor,
        string? proofDoorOverride,
        bool useXr,
        bool enableFirstPersonPresentation,
        string? actorScenePath,
        string? actorScenesManifestPath,
        bool proofEnableActor,
        bool buildCollision,
        bool applyCellEnvironment,
        IReadOnlySet<string> requiredReferenceFormIds)
    {
        var initialLoad = Stopwatch.StartNew();
        var (descriptors, edges) = ReadRoute(routeScenePath, routeSource);
        var activeDescriptor = descriptors.SingleOrDefault(value =>
            value.FormId.Equals(session.ActiveCellFormId, StringComparison.OrdinalIgnoreCase));
        if (activeDescriptor is null)
            throw new InvalidOperationException(
                $"Saved active CELL is outside the prepared route: {session.ActiveCellFormId}");
        var textureMemory = new RuntimeMaterialLoader.TextureMemoryStore();
        var activeContent = CellContentLoader.Load(
            activeDescriptor.ScenePath,
            parent,
            session,
            configuration,
            enableFirstPersonPresentation,
            activeDescriptor.Index == 0 ? actorScenePath : null,
            actorScenesManifestPath,
            proofEnableActor,
            buildCollision,
            activeDescriptor.RenderLayer,
            textureMemory);
        ValidateContent(activeContent, activeDescriptor);
        using var activeDocument = JsonDocument.Parse(
            File.ReadAllText(activeDescriptor.ScenePath));
        var activeSource = activeDocument.RootElement;
        var activeProofDoorId = proofDoorOverride ??
            activeSource.GetProperty("proof").GetProperty("doorReferenceFormId").GetString()!;
        if (!activeContent.Doors.TryGetValue(activeProofDoorId, out var proofDoor))
            throw new InvalidOperationException(
                $"Cell proof door was not loaded: {activeProofDoorId}");
        if (openProofDoor)
            proofDoor.SetOpen(true);

        if (enableFirstPersonPresentation && activeContent.StartingLoadout is { } loadout)
        {
            session.PrepareStartingLoadout(new GameplaySession.StartingWeapon(
                loadout.WeaponFormId,
                loadout.WeaponEditorId,
                loadout.AmmoFormId,
                loadout.AmmoEditorId,
                loadout.Damage,
                loadout.ClipSize,
                loadout.ReserveRounds,
                loadout.WeaponDisplayName,
                loadout.AmmoDisplayName));
        }
        var spawn = activeSource.GetProperty("spawn");
        var player = CellSceneLoader.BuildView(
            parent,
            spawn.GetProperty("yawGodotRadians").GetSingle(),
            activeContent,
            session,
            configuration,
            settings,
            useXr,
            applyCellEnvironment,
            false,
            null,
            gameplayVitals,
            activeDescriptor.RenderLayer,
            out var activeLights,
            out var worldEnvironment);
        session.ConfigureWorldContext(player, new[] { activeContent }, Array.Empty<(string, string)>());
        var activeSet = new CellActiveSet(
            new[]
            {
                new CellActiveSet.Space(
                    activeContent.FormId,
                    CellSceneLoader.ActivityRoots(activeContent),
                    activeLights),
            },
            Array.Empty<(string, string)>());
        var environmentSet = worldEnvironment is null
            ? null
            : CellEnvironmentSet.Create(
                worldEnvironment,
                new[] { activeContent },
                configuration);
        activeSet.Activate(session.ActiveCellFormId);
        environmentSet?.Activate(session.ActiveCellFormId);
        var actorGrounding = GameplayActorGrounding.Install(
            parent,
            configuration,
            activeSet,
            new[]
            {
                new GameplayActorGrounding.Space(
                    activeContent.FormId,
                    activeContent.Root,
                    activeDescriptor.RenderLayer,
                    activeContent.Actors),
            });
        player.CollisionMask = activeDescriptor.RenderLayer;
        if (enableFirstPersonPresentation)
        {
            if (activeContent.FirstPersonRig is not null)
                player.AttachFirstPersonRig(activeContent.FirstPersonRig, activeContent.UnitsToMeters);
            if (activeContent.HeldWeapon is not null)
                player.AttachHeldWeapon(
                    activeContent.HeldWeapon,
                    activeContent.StartingLoadout?.WeaponFormId
                        ?? throw new InvalidOperationException(
                            "Held weapon has no source loadout identity."),
                    activeContent.UnitsToMeters,
                    activeContent.MuzzlePosition);
        }

        var linkedCells = new List<CellSceneLoader.LinkedCell>();
        var portalLinks = new List<CellSceneLoader.PortalLink>();
        var pickups = new Dictionary<string, PickupInstance>(
            activeContent.Pickups,
            StringComparer.OrdinalIgnoreCase);
        var containers = new Dictionary<string, ContainerInstance>(
            activeContent.Containers,
            StringComparer.OrdinalIgnoreCase);
        var furniture = new Dictionary<string, FurnitureInstance>(
            activeContent.Furniture,
            StringComparer.OrdinalIgnoreCase);
        var pools = new Dictionary<string, PoolTableInstance>(
            activeContent.Pools,
            StringComparer.OrdinalIgnoreCase);
        var actors = activeContent.Actors.ToList();
        var route = new LazyLinkedCellRoute(
            parent,
            session,
            configuration,
            actorScenesManifestPath,
            proofEnableActor,
            buildCollision,
            applyCellEnvironment,
            textureMemory,
            descriptors,
            edges,
            requiredReferenceFormIds,
            new LoadedRouteCell(activeDescriptor, activeContent),
            linkedCells,
            portalLinks,
            pickups,
            containers,
            furniture,
            pools,
            actors,
            activeSet,
            environmentSet,
            actorGrounding);
        var portalTravel = new CellPortalTravel(
            portalLinks,
            session,
            activeSet,
            environmentSet,
            route.MaterializeAdjacent);
        route._portalTravel = portalTravel;
        player.ConfigurePortalTravel(portalTravel);
        _ = route.PrefetchInitialAdjacentAfterFirstFrame();
        initialLoad.Stop();
        GD.Print(
            $"OPENNV_LAZY_CELL_READY activeCell={activeContent.FormId} " +
            $"routeCells={descriptors.Count} materialized=1 " +
            $"elapsedMs={initialLoad.ElapsedMilliseconds}");

        return new CellSceneLoader.LoadedCell(
            activeContent.Root,
            activeContent.FormId,
            activeContent.EditorId,
            activeContent.OriginGameUnits,
            activeContent.UnitsToMeters,
            activeContent.Assets,
            activeContent.Textures,
            activeContent.MaterialBindings,
            activeContent.References,
            activeContent.Doors.Count,
            activeContent.Lighting.Lights.Count,
            activeContent.CollisionMeshes,
            activeContent.Surfaces,
            activeContent.Vertices,
            activeProofDoorId,
            proofDoor.IsOpen,
            proofDoor,
            player,
            session,
            pickups,
            containers,
            furniture,
            pools,
            actors,
            linkedCells,
            portalLinks,
            activeSet,
            environmentSet,
            actorGrounding,
            activeContent,
            route._initialAdjacentReady.Task,
            true,
            descriptors.Count);
    }

    private async Task PrefetchInitialAdjacentAfterFirstFrame()
    {
        try
        {
            await _parent.ToSignal(
                _parent.GetTree(),
                SceneTree.SignalName.ProcessFrame);
            MaterializeAdjacent(_session.ActiveCellFormId);
            MaterializeRequiredReferenceOwners();
            _initialAdjacentReady.SetResult();
        }
        catch (Exception exception)
        {
            _initialAdjacentReady.SetException(exception);
        }
    }

    private void MaterializeRequiredReferenceOwners()
    {
        if (_requiredReferenceFormIds.Count == 0)
            return;
        var ownership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in _descriptors)
        {
            if (descriptor.SceneSha256 is not null)
                VerifiedGltfLoader.VerifyHash(descriptor.ScenePath, descriptor.SceneSha256);
            using var document = JsonDocument.Parse(File.ReadAllText(descriptor.ScenePath));
            foreach (var reference in document.RootElement.GetProperty("references")
                         .EnumerateArray())
                AddReferenceOwner(
                    ownership,
                    reference.GetProperty("formId").GetString()!,
                    descriptor.FormId);
        }
        if (_actorScenesManifestPath is not null)
            foreach (var actor in CellActorLoader.LoadManifestEntries(
                         _actorScenesManifestPath))
                AddReferenceOwner(
                    ownership,
                    actor.ReferenceFormId,
                    actor.CellFormId);

        var ownerCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var referenceFormId in _requiredReferenceFormIds)
        {
            if (!ownership.TryGetValue(referenceFormId, out var ownerCell))
                throw new InvalidOperationException(
                    $"Prepared route has no owning CELL for required reference: " +
                    referenceFormId);
            if (!_descriptors.Any(value => value.FormId.Equals(
                    ownerCell,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"Required reference belongs to a CELL outside the prepared route: " +
                    referenceFormId);
            ownerCells.Add(ownerCell);
        }
        foreach (var ownerCell in ownerCells.Order(StringComparer.OrdinalIgnoreCase))
            MaterializePathTo(ownerCell);
    }

    private static void AddReferenceOwner(
        IDictionary<string, string> ownership,
        string referenceFormId,
        string cellFormId)
    {
        if (ownership.TryGetValue(referenceFormId, out var previous))
        {
            if (!previous.Equals(cellFormId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Prepared route reference has ambiguous CELL ownership: " +
                    referenceFormId);
            return;
        }
        ownership.Add(referenceFormId, cellFormId);
    }

    private void MaterializePathTo(string targetCellFormId)
    {
        if (_loaded.ContainsKey(targetCellFormId))
            return;
        var queue = new Queue<string>();
        var previous = new Dictionary<string, RouteStep?>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var loadedCell in _loaded.Keys)
        {
            queue.Enqueue(loadedCell);
            previous.Add(loadedCell, null);
        }
        while (queue.TryDequeue(out var current))
        {
            if (current.Equals(targetCellFormId, StringComparison.OrdinalIgnoreCase))
                break;
            foreach (var edge in _edges.Where(value => value.Contains(current)))
            {
                var next = edge.Other(current);
                if (previous.ContainsKey(next.FormId))
                    continue;
                previous.Add(next.FormId, new RouteStep(current, edge, next));
                queue.Enqueue(next.FormId);
            }
        }
        if (!previous.ContainsKey(targetCellFormId))
            throw new InvalidOperationException(
                $"Prepared route has no authored CELL path to required reference owner: " +
                targetCellFormId);
        var path = new List<RouteStep>();
        var cursor = targetCellFormId;
        while (previous[cursor] is { } step)
        {
            path.Add(step);
            cursor = step.PreviousCellFormId;
        }
        path.Reverse();
        foreach (var step in path)
            if (!_loaded.ContainsKey(step.Descriptor.FormId))
                Materialize(step.Edge, step.Descriptor, step.PreviousCellFormId);
    }

    private void MaterializeAdjacent(string cellFormId)
    {
        foreach (var edge in _edges.Where(value => value.Contains(cellFormId)).ToArray())
        {
            var other = edge.Other(cellFormId);
            if (_loaded.ContainsKey(other.FormId))
                continue;
            Materialize(edge, other, cellFormId);
        }
    }

    private void Materialize(
        RouteEdge edge,
        CellDescriptor descriptor,
        string fixedCellFormId)
    {
        var elapsed = Stopwatch.StartNew();
        var fixedCell = _loaded[fixedCellFormId];
        VerifiedGltfLoader.VerifyHash(descriptor.ScenePath, descriptor.SceneSha256!);
        var content = CellContentLoader.Load(
            descriptor.ScenePath,
            _parent,
            _session,
            _configuration,
            false,
            null,
            _actorScenesManifestPath,
            _proofEnableActor,
            _buildCollision,
            descriptor.RenderLayer,
            _textureMemory);
        ValidateContent(content, descriptor);
        var fixedDoorId = edge.From.FormId.Equals(
                fixedCellFormId,
                StringComparison.OrdinalIgnoreCase)
            ? edge.FromDoorFormId
            : edge.ToDoorFormId;
        var movingDoorId = edge.From.FormId.Equals(
                descriptor.FormId,
                StringComparison.OrdinalIgnoreCase)
            ? edge.FromDoorFormId
            : edge.ToDoorFormId;
        if (!fixedCell.Content.Doors.TryGetValue(fixedDoorId, out var fixedDoor) ||
            !content.Doors.TryGetValue(movingDoorId, out var movingDoor))
            throw new InvalidOperationException(
                $"Linked CELL portal doors are missing: {fixedDoorId} -> {movingDoorId}");
        if (fixedDoor.Destination is null || movingDoor.Destination is null)
            throw new InvalidOperationException(
                $"Linked CELL portal XTEL transforms are missing: {fixedDoorId} -> {movingDoorId}");
        var portalWasOpen = fixedDoor.IsOpen || movingDoor.IsOpen;
        fixedDoor.RestoreOpenState(false);
        movingDoor.RestoreOpenState(false);
        var fixedFrame = CellSceneLoader.BuildProofRay(fixedDoor, _configuration.Proof);
        var movingFrame = CellSceneLoader.BuildProofRay(movingDoor, _configuration.Proof);
        var fixedNormal = CellSceneLoader.HorizontalDoorNormal(fixedFrame);
        var movingNormal = CellSceneLoader.HorizontalDoorNormal(movingFrame);
        var targetNormal = movingNormal.Dot(fixedNormal) < 0.0f
            ? -fixedNormal
            : fixedNormal;
        var yawAlignment = MathF.Atan2(
            movingNormal.Cross(targetNormal).Y,
            movingNormal.Dot(targetNormal));
        content.Root.RotateY(yawAlignment);
        movingFrame = CellSceneLoader.BuildProofRay(movingDoor, _configuration.Proof);
        var fixedCenter = (fixedFrame.From + fixedFrame.To) / 2.0f;
        var movingCenter = (movingFrame.From + movingFrame.To) / 2.0f;
        content.Root.GlobalPosition += fixedCenter - movingCenter;
        var alignedMovingFrame = CellSceneLoader.BuildProofRay(movingDoor, _configuration.Proof);
        var alignedMovingCenter = (alignedMovingFrame.From + alignedMovingFrame.To) / 2.0f;
        var alignmentError = fixedCenter.DistanceTo(alignedMovingCenter);
        var normalAgreement = MathF.Abs(
            (fixedFrame.To - fixedFrame.From).Normalized().Dot(
                (alignedMovingFrame.To - alignedMovingFrame.From).Normalized()));
        if (alignmentError > _configuration.Proof.PortalAlignmentToleranceMeters)
            throw new InvalidOperationException(
                $"Linked CELL portal alignment failed: {alignmentError:F6} metres");
        if (normalAgreement < _configuration.Proof.PortalNormalAgreementMinimum)
            throw new InvalidOperationException(
                $"Linked CELL portal normals disagree: {normalAgreement:F6}");

        var lights = CellSceneLoader.AddCellLights(
            _parent,
            content,
            _configuration,
            descriptor.RenderLayer,
            true,
            _applyCellEnvironment);
        _environmentSet?.AddContent(content, _configuration);
        _activeSet.AddSpace(new CellActiveSet.Space(
            content.FormId,
            CellSceneLoader.ActivityRoots(content),
            lights));
        _activeSet.AddEdge(fixedCellFormId, content.FormId);
        _actorGrounding.AddSpace(new GameplayActorGrounding.Space(
            content.FormId,
            content.Root,
            descriptor.RenderLayer,
            content.Actors));
        _session.AddWorldContent(content, fixedCellFormId);
        AddUnique(_pickups, content.Pickups, "pickup");
        AddUnique(_containers, content.Containers, "container");
        AddUnique(_furniture, content.Furniture, "furniture");
        AddUnique(_pools, content.Pools, "pool table");
        _actors.AddRange(content.Actors);
        _linkedCells.Add(new CellSceneLoader.LinkedCell(content, descriptor.RenderLayer));
        _loaded.Add(content.FormId, new LoadedRouteCell(descriptor, content));

        var from = edge.From.FormId.Equals(content.FormId, StringComparison.OrdinalIgnoreCase)
            ? new LoadedRouteCell(descriptor, content)
            : fixedCell;
        var to = edge.To.FormId.Equals(content.FormId, StringComparison.OrdinalIgnoreCase)
            ? new LoadedRouteCell(descriptor, content)
            : fixedCell;
        var fromDoor = from.Content.Doors[edge.FromDoorFormId];
        var toDoor = to.Content.Doors[edge.ToDoorFormId];
        var fromFrame = CellSceneLoader.BuildProofRay(fromDoor, _configuration.Proof);
        var toFrame = CellSceneLoader.BuildProofRay(toDoor, _configuration.Proof);
        fromDoor.Link(toDoor);
        fromDoor.RestoreOpenState(portalWasOpen);
        var link = new CellSceneLoader.PortalLink(
            from.Content.FormId,
            from.Content.EditorId,
            from.Content.Root,
            from.Content.OriginGameUnits,
            from.Descriptor.RenderLayer,
            fromDoor,
            fromFrame,
            to.Content.FormId,
            to.Content.EditorId,
            to.Content.Root,
            to.Content.OriginGameUnits,
            to.Descriptor.RenderLayer,
            toDoor,
            toFrame,
            alignmentError,
            normalAgreement);
        _portalLinks.Add(link);
        _portalTravel!.AddLink(link);
        elapsed.Stop();
        GD.Print(
            $"OPENNV_LAZY_CELL_MATERIALIZED cell={content.FormId} " +
            $"adjacent={fixedCellFormId} materialized={_loaded.Count} " +
            $"elapsedMs={elapsed.ElapsedMilliseconds}");
    }

    private static void AddUnique<T>(
        IDictionary<string, T> target,
        IReadOnlyDictionary<string, T> source,
        string kind)
    {
        foreach (var pair in source)
            if (!target.TryAdd(pair.Key, pair.Value))
                throw new InvalidOperationException(
                    $"Lazy CELL route duplicates {kind}: {pair.Key}");
    }

    private static (IReadOnlyList<CellDescriptor>, IReadOnlyList<RouteEdge>) ReadRoute(
        string routeScenePath,
        JsonElement routeSource)
    {
        var rootCell = routeSource.GetProperty("cell");
        var descriptors = new List<CellDescriptor>
        {
            new(
                0,
                rootCell.GetProperty("formId").GetString()!,
                rootCell.GetProperty("editorId").GetString()!,
                routeSource.GetProperty("recipe").GetString()!,
                routeSource.GetProperty("recipeSha256").GetString()!,
                routeScenePath,
                null),
        };
        var edges = new List<RouteEdge>();
        if (!routeSource.TryGetProperty("linkedCells", out var links))
            return (descriptors, edges);
        foreach (var link in links.EnumerateArray())
        {
            var from = descriptors.Single(value => value.FormId.Equals(
                link.GetProperty("fromCellFormId").GetString(),
                StringComparison.OrdinalIgnoreCase));
            var linkedScenePath = VerifiedGltfLoader.ResolvePath(
                link.GetProperty("scene").GetString()!);
            var linkedSceneSha256 = link.GetProperty("sha256").GetString()!;
            var descriptor = new CellDescriptor(
                descriptors.Count,
                link.GetProperty("cellFormId").GetString()!,
                null,
                link.GetProperty("recipe").GetString()!,
                link.GetProperty("recipeSha256").GetString()!,
                linkedScenePath,
                linkedSceneSha256);
            descriptors.Add(descriptor);
            edges.Add(new RouteEdge(
                from,
                descriptor,
                link.GetProperty("fromDoorReferenceFormId").GetString()!,
                link.GetProperty("toDoorReferenceFormId").GetString()!));
        }
        return (descriptors, edges);
    }

    private static void ValidateContent(
        CellContentLoader.LoadedContent content,
        CellDescriptor descriptor)
    {
        if (!content.FormId.Equals(descriptor.FormId, StringComparison.OrdinalIgnoreCase) ||
            descriptor.EditorId is not null &&
            !content.EditorId.Equals(descriptor.EditorId, StringComparison.Ordinal) ||
            !content.RecipeId.Equals(descriptor.RecipeId, StringComparison.Ordinal) ||
            !content.RecipeSha256.Equals(
                descriptor.RecipeSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Lazy CELL content differs from its prepared route identity: " +
                $"{descriptor.FormId}");
    }

    private sealed record CellDescriptor(
        int Index,
        string FormId,
        string? EditorId,
        string RecipeId,
        string RecipeSha256,
        string ScenePath,
        string? SceneSha256)
    {
        internal uint RenderLayer
        {
            get
            {
                if (Index >= GodotPhysicsLayerCount)
                    throw new InvalidOperationException(
                        $"Prepared route exceeds Godot collision layers: {Index + 1}");
                return 1u << Index;
            }
        }
    }

    private sealed record RouteEdge(
        CellDescriptor From,
        CellDescriptor To,
        string FromDoorFormId,
        string ToDoorFormId)
    {
        internal bool Contains(string formId) =>
            From.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase) ||
            To.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase);

        internal CellDescriptor Other(string formId) =>
            From.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase) ? To : From;
    }

    private sealed record LoadedRouteCell(
        CellDescriptor Descriptor,
        CellContentLoader.LoadedContent Content);

    private sealed record RouteStep(
        string PreviousCellFormId,
        RouteEdge Edge,
        CellDescriptor Descriptor);
}
