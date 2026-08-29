using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Portals;

namespace OpenNV.Runtime;

internal static class CellSceneLoaderNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point20f = 0.20f;
    internal const float PresentationFloat0Point35f = 0.35f;
    internal const float PresentationFloat0Point48f = 0.48f;
    internal const float PresentationFloat0Point50f = 0.50f;
    internal const float PresentationFloat1Point15f = 1.15f;
    internal const float PresentationFloat1Point20f = 1.20f;
    internal const float PresentationFloat1Point25f = 1.25f;
}

internal static class CellSceneLoader
{
    private const string CellSceneSchema = "opennv-cell-scene/v13";

    internal static LoadedCell Load(
        string scenePath,
        Node3D parent,
        RuntimeConfiguration configuration,
        bool openProofDoor,
        string? proofDoorOverride = null,
        string? savePath = null,
        bool useXr = false,
        bool enableFirstPersonPresentation = true,
        string? actorScenePath = null,
        string? actorScenesManifestPath = null,
        bool proofEnableActor = false,
        bool buildCollision = true,
        bool applyCellEnvironment = true,
        bool loadExistingSave = true,
        bool showGameplayHud = true,
        bool useClassicDiorama = false,
        OwnedGameplayUiPresentation? gameplayUi = null,
        OpeningGameplayVitalsContract? gameplayVitals = null)
    {
        var resolvedScenePath = VerifiedGltfLoader.ResolvePath(scenePath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedScenePath));
        var source = document.RootElement;
        if (source.GetProperty("schema").GetString() != CellSceneSchema ||
            source.GetProperty("status").GetString() != "geometry-structure")
            throw new InvalidOperationException($"Unexpected OpenNV cell scene: {resolvedScenePath}");
        configuration.VerifyCompiledConfiguration(source);

        var cell = source.GetProperty("cell");
        var proofDoorId = proofDoorOverride ??
            source.GetProperty("proof").GetProperty("doorReferenceFormId").GetString()!;
        var objectiveOverride = source.TryGetProperty("concept", out var concept) &&
            concept.TryGetProperty("hudObjective", out var hudObjective)
                ? hudObjective.GetString()
                : null;
        var session = new GameplaySession();
        session.Configure(
            cell.GetProperty("formId").GetString()!,
            cell.GetProperty("editorId").GetString()!,
            proofDoorId,
            configuration,
            savePath,
            useXr,
            loadExistingSave,
            showGameplayHud,
            useClassicDiorama,
            objectiveOverride,
            gameplayUi,
            gameplayVitals);
        parent.AddChild(session);
        var main = CellContentLoader.Load(
            resolvedScenePath,
            parent,
            session,
            configuration,
            enableFirstPersonPresentation,
            actorScenePath,
            actorScenesManifestPath,
            proofEnableActor,
            buildCollision,
            1u);
        if (enableFirstPersonPresentation && main.StartingLoadout is { } loadout)
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

        var linkedCells = new List<LinkedCell>();
        var portalLinks = new List<PortalLink>();
        if (source.TryGetProperty("linkedCells", out var links))
        {
            var linkIndex = 0;
            foreach (var link in links.EnumerateArray())
            {
                var linkedScenePath = VerifiedGltfLoader.ResolvePath(link.GetProperty("scene").GetString()!);
                VerifiedGltfLoader.VerifyHash(linkedScenePath, link.GetProperty("sha256").GetString()!);
                var renderLayer = 1u << ++linkIndex;
                var linked = CellContentLoader.Load(
                    linkedScenePath,
                    parent,
                    session,
                    configuration,
                    false,
                    null,
                    actorScenesManifestPath,
                    proofEnableActor,
                    buildCollision,
                    renderLayer);
                if (!Mathf.IsEqualApprox(linked.UnitsToMeters, main.UnitsToMeters))
                    throw new InvalidOperationException("Linked CELL unit scales do not match.");
                var fromDoorId = link.GetProperty("fromDoorReferenceFormId").GetString()!;
                var toDoorId = link.GetProperty("toDoorReferenceFormId").GetString()!;
                var sourceRenderLayer = linkedCells.Count == 0
                    ? 1u
                    : linkedCells[^1].RenderLayer;
                var sourceContent = linkedCells.Count == 0
                    ? main
                    : linkedCells[^1].Content;
                if (!sourceContent.RecipeId.Equals(
                        link.GetProperty("fromRecipe").GetString(),
                        StringComparison.Ordinal) ||
                    !sourceContent.FormId.Equals(
                        link.GetProperty("fromCellFormId").GetString(),
                        StringComparison.OrdinalIgnoreCase) ||
                    !linked.RecipeId.Equals(
                        link.GetProperty("recipe").GetString(),
                        StringComparison.Ordinal) ||
                    !linked.FormId.Equals(
                        link.GetProperty("cellFormId").GetString(),
                        StringComparison.OrdinalIgnoreCase) ||
                    !linked.RecipeSha256.Equals(
                        link.GetProperty("recipeSha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Linked CELL recipe or CELL identity differs from the ordered route.");
                if (!sourceContent.Doors.TryGetValue(fromDoorId, out var fromDoor) ||
                    !linked.Doors.TryGetValue(toDoorId, out var toDoor))
                    throw new InvalidOperationException(
                        $"Linked CELL portal doors are missing: {fromDoorId} -> {toDoorId}");
                if (fromDoor.Destination is null || toDoor.Destination is null)
                    throw new InvalidOperationException(
                        $"Linked CELL portal XTEL transforms are missing: {fromDoorId} -> {toDoorId}");
                var portalWasOpen = fromDoor.IsOpen || toDoor.IsOpen;
                fromDoor.SetOpen(false);
                toDoor.SetOpen(false);
                var fromFrame = BuildProofRay(fromDoor, configuration.Proof);
                var toFrame = BuildProofRay(toDoor, configuration.Proof);
                var fromNormal = HorizontalDoorNormal(fromFrame);
                var toNormal = HorizontalDoorNormal(toFrame);
                var targetNormal = toNormal.Dot(fromNormal) < 0.0f
                    ? -fromNormal
                    : fromNormal;
                var yawAlignment = MathF.Atan2(
                    toNormal.Cross(targetNormal).Y,
                    toNormal.Dot(targetNormal));
                linked.Root.RotateY(yawAlignment);
                toFrame = BuildProofRay(toDoor, configuration.Proof);
                var fromCenter = (fromFrame.From + fromFrame.To) / 2.0f;
                var toCenter = (toFrame.From + toFrame.To) / 2.0f;
                var translation = fromCenter - toCenter;
                linked.Root.GlobalPosition += translation;
                var alignedToFrame = BuildProofRay(toDoor, configuration.Proof);
                var alignedToCenter = (alignedToFrame.From + alignedToFrame.To) / 2.0f;
                var alignmentError = fromCenter.DistanceTo(alignedToCenter);
                var normalAgreement = MathF.Abs(
                    (fromFrame.To - fromFrame.From).Normalized().Dot(
                        (alignedToFrame.To - alignedToFrame.From).Normalized()));
                if (alignmentError > configuration.Proof.PortalAlignmentToleranceMeters)
                    throw new InvalidOperationException(
                        $"Linked CELL portal alignment failed: {alignmentError:F6} metres");
                if (normalAgreement < configuration.Proof.PortalNormalAgreementMinimum)
                    throw new InvalidOperationException(
                        $"Linked CELL portal normals disagree: {normalAgreement:F6}");
                fromDoor.Link(toDoor);
                fromDoor.SetOpen(portalWasOpen);
                linkedCells.Add(new LinkedCell(linked, renderLayer));
                portalLinks.Add(new PortalLink(
                    sourceContent.FormId,
                    sourceContent.EditorId,
                    sourceContent.Root,
                    sourceContent.OriginGameUnits,
                    sourceRenderLayer,
                    fromDoor,
                    fromFrame,
                    linked.FormId,
                    linked.EditorId,
                    linked.Root,
                    linked.OriginGameUnits,
                    renderLayer,
                    toDoor,
                    alignedToFrame,
                    alignmentError,
                    normalAgreement));
            }
        }

        var allDoors = main.Doors
            .Concat(linkedCells.SelectMany(value => value.Content.Doors))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);
        if (!allDoors.TryGetValue(proofDoorId, out var proofDoor))
            throw new InvalidOperationException($"Cell proof door was not loaded: {proofDoorId}");
        if (openProofDoor)
            proofDoor.SetOpen(true);

        var spawn = source.GetProperty("spawn");
        var renderBounds = useClassicDiorama
            ? WorldRenderBounds(
                new[] { main.Root }.Concat(linkedCells.Select(value => value.Content.Root)))
            : (Aabb?)null;
        var player = BuildView(
            parent,
            spawn.GetProperty("yawGodotRadians").GetSingle(),
            main,
            session,
            configuration,
            useXr,
            applyCellEnvironment,
            useClassicDiorama,
            renderBounds);
        session.ConfigureWorldContext(
            player,
            new[] { main }.Concat(linkedCells.Select(value => value.Content)),
            portalLinks.Select(link => (link.FromCellFormId, link.ToCellFormId)));
        player.ConfigurePortalTravel(new CellPortalTravel(portalLinks, session));
        player.CollisionMask = main.FormId.Equals(
                session.ActiveCellFormId,
                StringComparison.OrdinalIgnoreCase)
            ? 1u
            : linkedCells.Single(value => value.Content.FormId.Equals(
                session.ActiveCellFormId,
                StringComparison.OrdinalIgnoreCase)).RenderLayer;
        if (enableFirstPersonPresentation)
        {
            if (main.FirstPersonRig is not null)
                player.AttachFirstPersonRig(main.FirstPersonRig, main.UnitsToMeters);
            if (main.HeldWeapon is not null)
                player.AttachHeldWeapon(
                    main.HeldWeapon,
                    main.UnitsToMeters,
                    main.MuzzlePosition);
        }
        foreach (var linked in linkedCells)
            AddCellLights(
                parent,
                linked.Content,
                configuration,
                linked.RenderLayer,
                true,
                applyCellEnvironment);

        var allPickups = main.Pickups
            .Concat(linkedCells.SelectMany(value => value.Content.Pickups))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);
        var allContainers = main.Containers
            .Concat(linkedCells.SelectMany(value => value.Content.Containers))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);
        var allPools = main.Pools
            .Concat(linkedCells.SelectMany(value => value.Content.Pools))
            .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);
        var allActors = main.Actors
            .Concat(linkedCells.SelectMany(value => value.Content.Actors))
            .ToArray();
        return new LoadedCell(
            main.Root,
            main.FormId,
            main.EditorId,
            main.OriginGameUnits,
            main.UnitsToMeters,
            main.Assets + linkedCells.Sum(value => value.Content.Assets),
            main.Textures + linkedCells.Sum(value => value.Content.Textures),
            main.MaterialBindings + linkedCells.Sum(value => value.Content.MaterialBindings),
            main.References + linkedCells.Sum(value => value.Content.References),
            allDoors.Count,
            main.Lighting.Lights.Count + linkedCells.Sum(value => value.Content.Lighting.Lights.Count),
            main.CollisionMeshes + linkedCells.Sum(value => value.Content.CollisionMeshes),
            main.Surfaces + linkedCells.Sum(value => value.Content.Surfaces),
            main.Vertices + linkedCells.Sum(value => value.Content.Vertices),
            proofDoorId,
            proofDoor.IsOpen,
            proofDoor,
            player,
            session,
            allPickups,
            allContainers,
            allPools,
            allActors,
            linkedCells,
            portalLinks,
            main);
    }

    private static CellPlayer BuildView(
        Node3D parent,
        float yaw,
        CellContentLoader.LoadedContent main,
        GameplaySession session,
        RuntimeConfiguration configuration,
        bool useXr,
        bool applyCellEnvironment,
        bool useClassicDiorama,
        Aabb? renderBounds)
    {
        var lighting = main.Lighting;
        Godot.Environment? environment = null;
        if (applyCellEnvironment)
        {
            environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = configuration.Renderer.BackgroundColorRgba.Color(),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = lighting.AmbientColor,
                AmbientLightEnergy = configuration.Renderer.AmbientEnergyScale,
                TonemapMode = RuntimeRendering.ParseToneMapper(configuration.Renderer.ToneMapper),
                FogEnabled = true,
                FogMode = Godot.Environment.FogModeEnum.Depth,
                FogLightColor = lighting.FogColor,
                FogLightEnergy = configuration.Renderer.FogLightEnergy,
                FogDensity = configuration.Renderer.FogDensity,
                FogDepthBegin = lighting.FogNearGameUnits * main.UnitsToMeters,
                FogDepthEnd = lighting.FogFarGameUnits * main.UnitsToMeters,
                FogDepthCurve = lighting.FogPower,
            };
            parent.AddChild(new WorldEnvironment { Environment = environment });
        }
        AddCellLights(parent, main, configuration, 1u, true, applyCellEnvironment);
        var player = new CellPlayer();
        player.Configure(yaw, session, configuration, useXr, useClassicDiorama);
        parent.AddChild(player);
        if (useClassicDiorama)
        {
            if (renderBounds is not Aabb bounds || environment is null)
                throw new InvalidOperationException(
                    "Classic Diorama requires render bounds and the CELL environment.");
            player.FrameClassicDiorama(bounds);
            environment.AmbientLightEnergy *= CellSceneLoaderNumericContracts.PresentationFloat1Point25f;
            player.Camera.AddChild(new DirectionalLight3D
            {
                Name = "ClassicDioramaCameraFill",
                LightColor = environment.AmbientLightColor.Lerp(Colors.White, CellSceneLoaderNumericContracts.PresentationFloat0Point20f),
                LightEnergy = Math.Clamp(environment.AmbientLightEnergy * CellSceneLoaderNumericContracts.PresentationFloat0Point50f, CellSceneLoaderNumericContracts.PresentationFloat0Point35f, CellSceneLoaderNumericContracts.PresentationFloat1Point20f),
                ShadowEnabled = false,
            });
            var cameraDistance = player.Camera.Position.Length();
            var cellSpan = MathF.Max(bounds.Size.X, bounds.Size.Z);
            environment.FogDepthBegin = MathF.Max(environment.FogDepthBegin, cameraDistance * CellSceneLoaderNumericContracts.PresentationFloat0Point48f);
            environment.FogDepthEnd = MathF.Max(
                environment.FogDepthEnd,
                cameraDistance + cellSpan * CellSceneLoaderNumericContracts.PresentationFloat1Point15f);
        }
        return player;
    }

    private static Aabb WorldRenderBounds(IEnumerable<Node3D> roots)
    {
        var minimum = new Vector3(
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity);
        var maximum = new Vector3(
            float.NegativeInfinity,
            float.NegativeInfinity,
            float.NegativeInfinity);
        var meshCount = 0;
        foreach (var root in roots)
        {
            foreach (var mesh in Descendants<MeshInstance3D>(root))
            {
                var bounds = mesh.GetAabb();
                if (bounds.Size.LengthSquared() <= 0.0f)
                    continue;
                foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                    foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                        foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                        {
                            var point = mesh.ToGlobal(new Vector3(x, y, z));
                            minimum = minimum.Min(point);
                            maximum = maximum.Max(point);
                        }
                meshCount++;
            }
        }
        if (meshCount == 0)
            throw new InvalidOperationException(
                "CELL has no renderable bounds for Classic Diorama framing.");
        return new Aabb(minimum, maximum - minimum);
    }

    private static void AddCellLights(
        Node3D parent,
        CellContentLoader.LoadedContent content,
        RuntimeConfiguration configuration,
        uint renderLayer,
        bool addAuthoredLights,
        bool applyEnvironmentLighting)
    {
        var lighting = content.Lighting;
        if (applyEnvironmentLighting)
        {
            RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(
                content.Root,
                lighting.AmbientColor,
                lighting.FogColor,
                lighting.FogNearGameUnits,
                lighting.FogFarGameUnits,
                lighting.FogPower,
                content.UnitsToMeters);
            RuntimeMaterialLoader.ApplyRetailActorLighting(
                content.Root,
                lighting.AmbientColor,
                lighting.FogColor,
                lighting.FogNearGameUnits,
                lighting.FogFarGameUnits,
                lighting.FogPower,
                content.UnitsToMeters);
            RuntimeMaterialLoader.ApplyRetailLandscapeLighting(
                content.Root,
                lighting.AmbientColor,
                lighting.FogColor,
                lighting.FogNearGameUnits,
                lighting.FogFarGameUnits,
                lighting.FogPower,
                content.UnitsToMeters);
            RuntimeMaterialLoader.ApplyRetailGrassDistanceScale(
                content.Root,
                content.UnitsToMeters);
            var surfaceToLight = RetailLighting.SurfaceToLightFromXcllDegrees(
                lighting.DirectionalRotationDegrees.X,
                lighting.DirectionalRotationDegrees.Y);
            parent.AddChild(new DirectionalLight3D
            {
                Name = $"CELL_{content.FormId}_Directional",
                Transform = new Transform3D(
                    RetailLighting.DirectionalLightBasis(surfaceToLight),
                    Vector3.Zero),
                LightColor = lighting.DirectionalColor,
                LightEnergy = lighting.DirectionalFade *
                    configuration.Renderer.DirectionalEnergyScale,
                ShadowEnabled = configuration.ActorReview.DirectionalShadows,
                LightCullMask = renderLayer,
            });
        }
        if (!addAuthoredLights)
            return;
        foreach (var light in lighting.Lights)
        {
            parent.AddChild(new OmniLight3D
            {
                Name = $"LIGH_{light.FormId}_{light.EditorId}",
                Position = content.Root.ToGlobal(light.PositionGodotUnits),
                LightColor = light.Color,
                LightEnergy = MathF.Max(
                    configuration.Renderer.MinimumPointLightEnergy,
                    light.Intensity * configuration.Renderer.PointLightEnergyScale),
                OmniRange = RetailLighting.PointShaderRadius(light.RadiusMeters),
                OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap,
                ShadowEnabled = configuration.Renderer.AuthoredPointLightShadows,
                LightCullMask = renderLayer,
            });
        }
    }

    internal static DoorRay BuildProofRay(DoorInstance door, ProofConfiguration proof)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var meshCount = 0;
        foreach (var mesh in Descendants<MeshInstance3D>(door))
        {
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = door.ToLocal(mesh.ToGlobal(new Vector3(x, y, z)));
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
            meshCount++;
        }
        if (meshCount == 0)
            throw new InvalidOperationException("Proof door contains no renderable mesh.");

        var size = maximum - minimum;
        var center = (minimum + maximum) / 2.0f;
        var normal = size.X <= size.Z ? Vector3.Right : Vector3.Back;
        var thickness = MathF.Min(size.X, size.Z);
        var reach = MathF.Max(
            thickness * proof.DoorRayThicknessMultiplier,
            proof.DoorRayMinimumReachGameUnits);
        return new DoorRay(
            door.ToGlobal(center - normal * reach),
            door.ToGlobal(center + normal * reach),
            size,
            normal);
    }

    private static Vector3 HorizontalDoorNormal(DoorRay frame)
    {
        var normal = frame.To - frame.From;
        normal.Y = 0.0f;
        if (normal.IsZeroApprox())
            throw new InvalidOperationException("Door portal has no horizontal normal.");
        return normal.Normalized();
    }

    internal static RayHit CastProofRay(
        PhysicsDirectSpaceState3D space,
        DoorInstance door,
        DoorRay ray,
        uint collisionMask = uint.MaxValue)
    {
        var query = PhysicsRayQueryParameters3D.Create(ray.From, ray.To);
        query.CollisionMask = collisionMask;
        var hit = space.IntersectRay(query);
        if (hit.Count == 0)
            return new RayHit(false, false, "", Vector3.Zero);
        var collider = hit["collider"].AsGodotObject() as Node;
        return new RayHit(
            true,
            collider is not null && door.IsAncestorOf(collider),
            collider?.GetPath().ToString() ?? "unknown",
            hit["position"].AsVector3());
    }

    internal static FloorHit CastSpawnFloor(
        PhysicsDirectSpaceState3D space,
        ProofConfiguration proof,
        uint collisionMask,
        Rid excludedBody)
        => CastFloorAt(space, proof, collisionMask, excludedBody, Vector3.Zero);

    internal static FloorHit CastFloorAt(
        PhysicsDirectSpaceState3D space,
        ProofConfiguration proof,
        uint collisionMask,
        Rid excludedBody,
        Vector3 origin)
    {
        var query = PhysicsRayQueryParameters3D.Create(
            origin + Vector3.Up * proof.SpawnFloorRayStartMeters,
            origin + Vector3.Up * proof.SpawnFloorRayEndMeters,
            collisionMask);
        query.Exclude = new Godot.Collections.Array<Rid> { excludedBody };
        var hit = space.IntersectRay(query);
        if (hit.Count == 0)
            return new FloorHit(false, float.NaN, Vector3.Zero, "", null);
        var collider = hit["collider"].AsGodotObject() as Node;
        return new FloorHit(
            true,
            hit["position"].AsVector3().Y,
            hit["normal"].AsVector3(),
            collider?.GetPath().ToString() ?? "unknown",
            collider);
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    internal readonly record struct LoadedCell(
        Node3D Root,
        string FormId,
        string EditorId,
        Vector3 OriginGameUnits,
        float UnitsToMeters,
        int Assets,
        int Textures,
        int MaterialBindings,
        int References,
        int Doors,
        int AuthoredLights,
        int CollisionMeshes,
        int Surfaces,
        int Vertices,
        string ProofDoorFormId,
        bool ProofDoorOpen,
        DoorInstance ProofDoor,
        CellPlayer Player,
        GameplaySession Session,
        IReadOnlyDictionary<string, PickupInstance> Pickups,
        IReadOnlyDictionary<string, ContainerInstance> Containers,
        IReadOnlyDictionary<string, PoolTableInstance> Pools,
        IReadOnlyList<CellActorLoader.PlacedActor> Actors,
        IReadOnlyList<LinkedCell> LinkedCells,
        IReadOnlyList<PortalLink> PortalLinks,
        CellContentLoader.LoadedContent MainContent)
    {
        internal Vector3 GameToCellUnits(Vector3 position) => new(
            position.X - OriginGameUnits.X,
            position.Z - OriginGameUnits.Z,
            -(position.Y - OriginGameUnits.Y));

        internal Vector3 CellToGameUnits(Vector3 position) => new(
            position.X + OriginGameUnits.X,
            -position.Z + OriginGameUnits.Y,
            position.Y + OriginGameUnits.Z);

        internal Vector3 GameToWorld(Vector3 position) => Root.ToGlobal(GameToCellUnits(position));
    }

    internal readonly record struct LinkedCell(CellContentLoader.LoadedContent Content, uint RenderLayer);

    internal readonly record struct PortalLink(
        string FromCellFormId,
        string FromCellEditorId,
        Node3D FromRoot,
        Vector3 FromOriginGameUnits,
        uint FromCollisionLayer,
        DoorInstance FromDoor,
        DoorRay FromFrame,
        string ToCellFormId,
        string ToCellEditorId,
        Node3D ToRoot,
        Vector3 ToOriginGameUnits,
        uint ToCollisionLayer,
        DoorInstance ToDoor,
        DoorRay ToFrame,
        float AlignmentErrorMeters,
        float NormalAgreement);

    internal readonly record struct DoorRay(Vector3 From, Vector3 To, Vector3 LocalSize, Vector3 LocalNormal);

    internal readonly record struct RayHit(
        bool Hit,
        bool HitProofDoor,
        string ColliderPath,
        Vector3 Position);

    internal readonly record struct FloorHit(
        bool Hit,
        float Y,
        Vector3 Normal,
        string ColliderPath,
        Node? Collider);
}
