using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class Fo1HexProof
{
    internal static async Task Run(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var camera = loaded.Camera;
            var initialYaw = camera.TargetYawRadians;
            var initialPitch = camera.TargetPitchRadians;
            var initialSize = camera.TargetSizeMeters;
            var initialPosition = camera.Position;
            var firstPersonEyeErrorMeters = 0.0f;
            var firstPersonForwardAlignment = 0.0f;
            var firstPersonContinuousMoveMeters = 0.0f;
            var firstPersonApUnchanged = false;
            var firstPersonMissShots = 0;
            var firstPersonHitConfirmed = false;
            var firstPersonHitDamage = 0;
            var firstPersonPitchBeforeMouseUp = 0.0f;
            var firstPersonPitchAfterMouseUp = 0.0f;
            var firstPersonForwardYAfterMouseUp = 0.0f;

            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Middle,
                Pressed = true,
            });
            camera._UnhandledInput(new InputEventMouseMotion
            {
                Relative = new Vector2(36.0f, -18.0f),
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Middle,
                Pressed = false,
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right,
                Pressed = true,
            });
            camera._UnhandledInput(new InputEventMouseMotion
            {
                Relative = new Vector2(52.0f, 24.0f),
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right,
                Pressed = false,
            });
            camera._UnhandledInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.WheelUp,
                Pressed = true,
                Position = new Vector2(640.0f, 360.0f),
            });
            if (Mathf.IsEqualApprox(camera.TargetYawRadians, initialYaw) ||
                Mathf.IsEqualApprox(camera.TargetPitchRadians, initialPitch) ||
                camera.TargetSizeMeters >= initialSize ||
                camera.Position.IsEqualApprox(initialPosition) ||
                camera.OrbitDragging || camera.PanDragging)
                throw new InvalidOperationException("Fallout tactical mouse camera proof failed.");
            camera.SetExplorationMode(true);
            if (!camera.ExplorationMode ||
                camera.Camera.Projection != Camera3D.ProjectionType.Perspective)
                throw new InvalidOperationException("Fallout third-person camera did not activate.");
            var heldWeapon = loaded.Session.OwnedPlayerWeapon
                ?? throw new InvalidOperationException(
                    "Owned Vault Dweller has no third-person held weapon.");
            if (!heldWeapon.Root.IsVisibleInTree() || heldWeapon.BoneName != "Bip01 R Hand" ||
                heldWeapon.FormId != "0000434f" || heldWeapon.Surfaces != 7 ||
                heldWeapon.MaterialBindings != heldWeapon.Surfaces)
                throw new InvalidOperationException(
                    "Fallout third-person 10mm pistol attachment gate failed.");
            var heldMeleeWeapon = loaded.Session.OwnedPlayerMeleeWeapon
                ?? throw new InvalidOperationException(
                    "Owned Vault Dweller has no third-person melee weapon.");
            if (heldMeleeWeapon.Root.IsVisibleInTree() ||
                heldMeleeWeapon.BoneName != "Bip01 R Hand" ||
                heldMeleeWeapon.GameplayPid != "00000004" ||
                heldMeleeWeapon.Surfaces != 1 ||
                heldMeleeWeapon.MaterialBindings != heldMeleeWeapon.Surfaces)
                throw new InvalidOperationException(
                    "Fallout third-person knife attachment gate failed.");
            var hoverMarker = host.FindChild(
                "HoveredFalloutHex",
                true,
                false) as MeshInstance3D;
            if (hoverMarker?.Mesh is null ||
                hoverMarker.Mesh.GetAabb().Size.X <= hoverMarker.Mesh.GetAabb().Size.Z)
                throw new InvalidOperationException(
                    "Fallout selector ring is not aligned to the authoritative flat-top hex basis.");
            loaded.Session.SetHoveredTile(loaded.EntryTile);
            if (!hoverMarker.Visible)
                throw new InvalidOperationException(
                    "Fallout tactical selector did not follow the visible mouse state.");
            var fpsSourceOverlay = host.FindChild(
                "FO1_SOURCE_STATIC_SPRITE_OVERLAY",
                true,
                false) as Node3D;
            var fpsContinuousFloor = host.FindChild(
                "FO1_OWNED_CONTINUOUS_CAVE_FLOOR",
                true,
                false) as GeometryInstance3D;
            if (fpsSourceOverlay is null || fpsContinuousFloor is null ||
                fpsSourceOverlay.Visible || !fpsContinuousFloor.Visible)
                throw new InvalidOperationException(
                    "Fallout FPS presentation precondition is not the owned 3D cave.");
            loaded.Session.ToggleSourceOverlay();
            if (!fpsSourceOverlay.Visible || fpsContinuousFloor.Visible)
                throw new InvalidOperationException(
                    "Fallout tactical source-reference toggle did not activate.");
            camera.SetFirstPersonMode(true);
            if (loaded.Session.HoveredTile >= 0 || hoverMarker.Visible)
                throw new InvalidOperationException(
                    "Fallout FPS retained a tactical selector at the captured mouse center.");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (heldWeapon.Root.IsVisibleInTree())
                throw new InvalidOperationException(
                    "Fallout third-person held weapon leaked into the clean FPS presentation.");
            GD.Print(
                "OPENNV_FO1_SELECTOR_PROOF_PASS tactical=mouse-projected " +
                "fps=hidden basis=authoritative-flat-top");
            var expectedEye = loaded.Session.PlayerToken.GlobalPosition +
                Vector3.Up * camera.FirstPersonEyeHeightMeters;
            firstPersonEyeErrorMeters = camera.FirstPersonEyePosition.DistanceTo(expectedEye);
            var expectedForward = -loaded.Session.PlayerToken.GlobalBasis.Z;
            expectedForward.Y = 0.0f;
            var actualForward = camera.FirstPersonForward;
            actualForward.Y = 0.0f;
            firstPersonForwardAlignment = actualForward.Normalized().Dot(expectedForward.Normalized());
            if (!camera.FirstPersonMode || !camera.ExplorationMode ||
                loaded.Session.PlayerToken.Visible ||
                camera.Camera.Projection != Camera3D.ProjectionType.Perspective ||
                MathF.Abs(camera.Camera.Fov - camera.FirstPersonFovDegrees) > 0.0001f ||
                firstPersonEyeErrorMeters > 0.0001f || firstPersonForwardAlignment < 0.9999f)
                throw new InvalidOperationException(
                    $"Fallout first-person camera gate failed: eyeError={firstPersonEyeErrorMeters:F6} " +
                    $"forward={firstPersonForwardAlignment:F6} visible={loaded.Session.PlayerToken.Visible}.");
            if (fpsSourceOverlay.Visible || !fpsContinuousFloor.Visible)
                throw new InvalidOperationException(
                    "Fallout first-person mode did not suppress floating 2.5D source cards.");
            loaded.Session.ToggleSourceOverlay();
            if (fpsSourceOverlay.Visible || !fpsContinuousFloor.Visible)
                throw new InvalidOperationException(
                    "Fallout source-reference toggle escaped the first-person 3D-only gate.");
            firstPersonPitchBeforeMouseUp = camera.TargetPitchRadians;
            camera.ApplyFirstPersonLook(new Vector2(0.0f, -24.0f));
            firstPersonPitchAfterMouseUp = camera.TargetPitchRadians;
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            firstPersonForwardYAfterMouseUp = camera.FirstPersonForward.Y;
            if (firstPersonPitchAfterMouseUp <= firstPersonPitchBeforeMouseUp ||
                firstPersonForwardYAfterMouseUp <= 0.0f)
                throw new InvalidOperationException(
                    $"Fallout FPS mouse-up look is inverted: pitch=" +
                    $"{firstPersonPitchBeforeMouseUp:F4}->{firstPersonPitchAfterMouseUp:F4} " +
                    $"forwardY={firstPersonForwardYAfterMouseUp:F4}.");
            var fpsStart = loaded.Session.PlayerToken.Position;
            var fpsAp = loaded.Session.ActionPoints;
            var fpsNeighbor = Fo1HexMath.Neighbors(loaded.Session.PlayerTile)
                .First(loaded.Session.CanWalk);
            var fpsDirection = Fo1HexMath.Center(fpsNeighbor) - Fo1HexMath.Center(loaded.Session.PlayerTile);
            var fpsYaw = MathF.Atan2(-fpsDirection.X, -fpsDirection.Z);
            camera.SetOrbitDegrees(Mathf.RadToDeg(fpsYaw), -1.0f);
            if (!camera.MoveFirstPerson(new Vector2(0.0f, 1.0f), 0.08f))
                throw new InvalidOperationException("Fallout continuous FPS movement was rejected.");
            firstPersonContinuousMoveMeters =
                loaded.Session.PlayerToken.Position.DistanceTo(fpsStart);
            firstPersonApUnchanged = loaded.Session.ActionPoints == fpsAp;
            var firstPersonMissDirection = loaded.Session.FindClearFirstPersonDirection(
                camera.FirstPersonEyePosition);
            _ = loaded.Session.FireFirstPerson(
                camera.FirstPersonEyePosition,
                firstPersonMissDirection);
            firstPersonMissShots = loaded.Session.FpsShots;
            if (firstPersonContinuousMoveMeters < 0.20f || !firstPersonApUnchanged ||
                loaded.Session.PlayerHexCenterErrorMeters < 0.20f ||
                firstPersonMissShots != 1 || loaded.Session.FpsHits != 0)
                throw new InvalidOperationException(
                    $"Fallout FPS contract failed: moved={firstPersonContinuousMoveMeters:F4} " +
                    $"AP={loaded.Session.ActionPoints}/{fpsAp} " +
                    $"centerOffset={loaded.Session.PlayerHexCenterErrorMeters:F4} " +
                    $"shots={firstPersonMissShots} hits={loaded.Session.FpsHits}.");
            await host.ToSignal(
                host.GetTree().CreateTimer(
                    loaded.RuntimeProfile.Gameplay.FirstPersonShotCooldownSeconds),
                SceneTreeTimer.SignalName.Timeout);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var firstPersonTarget = loaded.Session.Mobs
                .Where(mob => mob.Alive)
                .OrderBy(mob => Fo1HexMath.Distance(loaded.Session.PlayerTile, mob.Tile))
                .ThenBy(mob => mob.Serial)
                .First();
            var firstPersonTargetHp = firstPersonTarget.HitPoints;
            var firstPersonAim = firstPersonTarget.GlobalPosition +
                Vector3.Up * loaded.RuntimeProfile.Gameplay.FirstPersonTargetHeightMeters -
                camera.FirstPersonEyePosition;
            firstPersonHitConfirmed = loaded.Session.FireFirstPerson(
                camera.FirstPersonEyePosition,
                firstPersonAim.Normalized());
            firstPersonHitDamage = firstPersonTargetHp - firstPersonTarget.HitPoints;
            if (!firstPersonHitConfirmed || firstPersonHitDamage <= 0 ||
                loaded.Session.FpsHits != 1)
                throw new InvalidOperationException(
                    "Fallout continuous FPS ranged-hit proof failed.");
            camera.SetExplorationMode(false);
            if (!fpsSourceOverlay.Visible || fpsContinuousFloor.Visible)
                throw new InvalidOperationException(
                    "Fallout tactical source-reference preference did not survive FPS presentation.");
            loaded.Session.ToggleSourceOverlay();
            if (camera.ExplorationMode || camera.FirstPersonMode ||
                !loaded.Session.PlayerToken.Visible ||
                !heldWeapon.Root.IsVisibleInTree() ||
                heldMeleeWeapon.Root.IsVisibleInTree() ||
                camera.Camera.Projection != Camera3D.ProjectionType.Orthogonal ||
                fpsSourceOverlay.Visible || !fpsContinuousFloor.Visible ||
                loaded.Session.PlayerHexCenterErrorMeters > 0.0001f)
                throw new InvalidOperationException(
                    "Fallout perspective cameras did not preserve the tactical return path.");
            var worldEnvironment = host.FindChild(
                "WorldEnvironment",
                true,
                false) as WorldEnvironment;
            var environment = worldEnvironment?.Environment;
            var backgroundLuminance =
                0.2126f * loaded.Atmosphere.BackgroundColor.R +
                0.7152f * loaded.Atmosphere.BackgroundColor.G +
                0.0722f * loaded.Atmosphere.BackgroundColor.B;
            if (loaded.Atmosphere.Schema != "opennv-fo1-cave-atmosphere/v1" ||
                worldEnvironment is null || environment is null ||
                !environment.FogEnabled || !environment.VolumetricFogEnabled ||
                !loaded.Atmosphere.VolumetricFogEnabled ||
                loaded.Atmosphere.FogDensity is < 0.015f or > 0.06f ||
                loaded.Atmosphere.VolumetricFogDensity is < 0.005f or > 0.03f ||
                loaded.Atmosphere.VolumetricFogLengthMeters < 20.0f ||
                loaded.Atmosphere.PracticalLights < 3 ||
                loaded.Atmosphere.DirectionalLights < 1 ||
                loaded.Atmosphere.LocalFogVolumes < 3 ||
                backgroundLuminance < 0.01f ||
                loaded.RuntimeProfile.Cutaway.TacticalEnvelopeCutHeightMeters < 4.5f)
                throw new InvalidOperationException(
                    $"Fallout cave atmosphere gate failed: fog={loaded.Atmosphere.FogDensity:F3} " +
                    $"volumetric={loaded.Atmosphere.VolumetricFogDensity:F3} " +
                    $"background={backgroundLuminance:F3} practical={loaded.Atmosphere.PracticalLights} " +
                    $"localFog={loaded.Atmosphere.LocalFogVolumes} " +
                    $"envelopeCut={loaded.RuntimeProfile.Cutaway.TacticalEnvelopeCutHeightMeters:F2}.");
            var sourceSprites = Descendants<Sprite3D>(host).ToArray();
            var sourceOverlay = host.FindChild("FO1_SOURCE_STATIC_SPRITE_OVERLAY", true, false) as Node3D;
            var caveMeshes = new[]
            {
                host.FindChild("V13ENT_FIXED_3D_CAVE_GEOMETRY", true, false) as GeometryInstance3D,
                host.FindChild("V13ENT_3D_WALL_BLOCKERS", true, false) as GeometryInstance3D,
                host.FindChild("V13ENT_3D_ROCK_BLOCKERS", true, false) as GeometryInstance3D,
            };
            var actorSprites = sourceSprites.Where(sprite =>
                sprite.Name == "SourceCritterSprite" || sprite.Name == "VaultDwellerSourceSprite").ToArray();
            var staticSprites = sourceSprites.Where(sprite =>
                sprite.Name.ToString().StartsWith("FO1_OBJ_", StringComparison.Ordinal)).ToArray();
            var creatureRoots = Descendants<Node3D>(host)
                .Where(node => node.Name == "OwnedNVCrGiantRat")
                .ToArray();
            var creatureSkeletons = creatureRoots.Sum(root => Descendants<Skeleton3D>(root).Count());
            var creaturePlayers = creatureRoots.Sum(root => Descendants<AnimationPlayer>(root).Count());
            var hiddenGoreMeshes = loaded.Session.Mobs.Sum(mob => mob.CreatureHiddenGoreMeshes);
            var ownedCreaturePresentation = loaded.CreatureAnimations > 0;
            var ownedPlayerPresentation = loaded.PlayerActor is not null;
            var playerRoot = host.FindChild("OwnedVaultDweller", true, false) as Node3D;
            var playerSourceSprite = host.FindChild(
                "VaultDwellerSourceSprite",
                true,
                false) as Sprite3D;
            var playerSkeletons = playerRoot is null
                ? 0
                : Descendants<Skeleton3D>(playerRoot).Count();
            var playerAnimationPlayers = playerRoot is null
                ? 0
                : Descendants<AnimationPlayer>(playerRoot).Count();
            var ownedCaveContainer = host.FindChild("FO1_OWNED_CAVE_COMPOSITION", true, false) as Node3D;
            var ownedCavePresentation = loaded.OwnedCave.Instances > 0;
            var expectedGroundedRockInstances =
                loaded.OwnedCave.Roles.GetValueOrDefault("large-rock") +
                loaded.OwnedCave.Roles.GetValueOrDefault("small-rock") +
                loaded.OwnedCave.Roles.GetValueOrDefault("stalagmite");
            var continuousFloor = host.FindChild(
                "FO1_OWNED_CONTINUOUS_CAVE_FLOOR",
                true,
                false) as MeshInstance3D;
            var hexOverlay = host.FindChild(
                "V13ENT_200X200_HEX_GRID",
                true,
                false) as MeshInstance3D;
            var sourceRatSprites = sourceSprites.Where(sprite => sprite.Name == "SourceCritterSprite").ToArray();
            var maximumAnchorError = sourceSprites.Length == 0
                ? float.PositiveInfinity
                : sourceSprites.Max(sprite => MathF.Abs(sprite.GlobalPosition.Y - 0.015f));
            if (sourceOverlay is null || sourceOverlay.Visible == ownedCreaturePresentation ||
                caveMeshes.Any(mesh => mesh is null || mesh.Visible) ||
                sourceSprites.Length != loaded.SpritePlacements + 1 ||
                actorSprites.Length != loaded.CombatMobs + 1 ||
                actorSprites.Any(sprite => sprite.Billboard != BaseMaterial3D.BillboardModeEnum.FixedY) ||
                staticSprites.Length != loaded.SpritePlacements - loaded.CombatMobs ||
                staticSprites.Any(sprite => sprite.Billboard != BaseMaterial3D.BillboardModeEnum.Disabled) ||
                staticSprites.Any(sprite => MathF.Abs(
                    sprite.RotationDegrees.Y -
                    loaded.RuntimeProfile.Generation.StaticWorldSpriteYawDegrees) > 0.0001f) ||
                maximumAnchorError > 0.0001f ||
                ownedCreaturePresentation &&
                (creatureRoots.Length != loaded.CombatMobs ||
                    creatureSkeletons != loaded.CombatMobs ||
                    creaturePlayers != loaded.CombatMobs ||
                    hiddenGoreMeshes !=
                        loaded.CombatMobs * loaded.RuntimeProfile.Mob.ExpectedIntactHiddenMeshes ||
                    sourceRatSprites.Any(sprite => sprite.Visible)) ||
                ownedPlayerPresentation &&
                (playerRoot is null ||
                    playerSourceSprite is null || playerSourceSprite.Visible ||
                    playerSkeletons != 1 || playerAnimationPlayers != 1 ||
                    string.IsNullOrWhiteSpace(loaded.PlayerActor!.Value.FormId) ||
                    loaded.PlayerActor.Value.Meshes !=
                        loaded.PlayerActor.Value.AuthoredSurfaces ||
                    loaded.PlayerActor.Value.AuthoredSurfaces < 1 ||
                    loaded.PlayerActor.Value.AuthoredTextures < 1 ||
                    loaded.PlayerActor.Value.Bounds.Size.Y <= 0.0f) ||
                ownedCavePresentation &&
                (ownedCaveContainer is null ||
                    ownedCaveContainer.GetChildCount() != loaded.OwnedCave.Instances ||
                    loaded.OwnedCave.Assets < 6 || loaded.OwnedCave.MeshInstances < 100 ||
                    continuousFloor is null || !continuousFloor.Visible ||
                    continuousFloor.Mesh?.GetSurfaceCount() != 1 ||
                    loaded.OwnedCave.ContinuousFloorHexes != loaded.RenderedFloorTiles * 4 ||
                    loaded.OwnedCave.ContinuousFloorTriangles !=
                        loaded.OwnedCave.ContinuousFloorHexes * 6 ||
                    loaded.OwnedCave.ContinuousFloorMeshInstances != 1 ||
                    loaded.OwnedCave.Roles.GetValueOrDefault("vault-portal") != 1 ||
                    expectedGroundedRockInstances < 1 ||
                    loaded.OwnedCave.GroundedInstances != expectedGroundedRockInstances ||
                    loaded.OwnedCave.MinimumGroundSeatDepthMeters < 0.025f ||
                    loaded.OwnedCave.MaximumGroundErrorMeters >
                        loaded.OwnedCave.GroundingToleranceMeters ||
                    loaded.OwnedCave.GroundingToleranceMeters > 0.002f) ||
                hexOverlay is null || hexOverlay.Visible ||
                hexOverlay.Mesh?.GetSurfaceCount() != 1 ||
                hexOverlay.GetMeta("hex_count").AsInt32() != loaded.WalkableHexes ||
                hexOverlay.GetMeta("edge_count").AsInt32() <= loaded.WalkableHexes ||
                hexOverlay.MaterialOverride is not StandardMaterial3D gridMaterial ||
                gridMaterial.NoDepthTest ||
                gridMaterial.Transparency != BaseMaterial3D.TransparencyEnum.Disabled)
                throw new InvalidOperationException(
                    $"Fallout source-sprite ground anchor failed: sprites={sourceSprites.Length} " +
                    $"expected={loaded.SpritePlacements + 1} maxError={maximumAnchorError:F6} " +
                    $"actors={actorSprites.Length}/{loaded.CombatMobs + 1} " +
                    $"static={staticSprites.Length}/{loaded.SpritePlacements - loaded.CombatMobs}/" +
                    $"billboard={staticSprites.Count(sprite => sprite.Billboard != BaseMaterial3D.BillboardModeEnum.Disabled)}/" +
                    $"yaw={staticSprites.Count(sprite => MathF.Abs(sprite.RotationDegrees.Y - loaded.RuntimeProfile.Generation.StaticWorldSpriteYawDegrees) > 0.0001f)} " +
                    $"legacy={string.Join(',', caveMeshes.Select(mesh => mesh?.Visible))} " +
                    $"creatures={creatureRoots.Length}/{creatureSkeletons}/{creaturePlayers} " +
                    $"player={playerSkeletons}/{playerAnimationPlayers}/" +
                    $"{loaded.PlayerActor?.Animations}/{loaded.PlayerActor?.Bounds.Size.Y:F3}/" +
                    $"{loaded.PlayerActor?.AuthoredSurfaces}/{loaded.PlayerActor?.AuthoredTextures}/" +
                    $"sprite={playerSourceSprite?.Visible} rats={sourceRatSprites.Count(sprite => sprite.Visible)} " +
                    $"gore={hiddenGoreMeshes} overlay={sourceOverlay?.Visible} " +
                    $"cave={ownedCaveContainer?.GetChildCount()}/{loaded.OwnedCave.Instances}/" +
                    $"{loaded.OwnedCave.Assets}/{loaded.OwnedCave.MeshInstances}/" +
                    $"ground={loaded.OwnedCave.GroundedInstances}/{expectedGroundedRockInstances}/" +
                    $"{loaded.OwnedCave.MinimumGroundSeatDepthMeters:F4}/" +
                    $"{loaded.OwnedCave.MaximumGroundErrorMeters:F4}/" +
                    $"{loaded.OwnedCave.GroundingToleranceMeters:F4} " +
                    $"floor={continuousFloor?.Visible}/{continuousFloor?.Mesh?.GetSurfaceCount()}/" +
                    $"{loaded.OwnedCave.ContinuousFloorHexes}/" +
                    $"{loaded.OwnedCave.ContinuousFloorTriangles} " +
                    $"grid={hexOverlay?.Visible}/{hexOverlay?.GetMeta("hex_count")}/" +
                    $"{hexOverlay?.GetMeta("edge_count")}/" +
                    $"{(hexOverlay?.MaterialOverride as StandardMaterial3D)?.NoDepthTest}/" +
                    $"{(hexOverlay?.MaterialOverride as StandardMaterial3D)?.Transparency}");

            loaded.Session.ToggleGrid();
            if (!hexOverlay.Visible)
                throw new InvalidOperationException("Fallout optional hex overlay did not become visible.");
            loaded.Session.ToggleGrid();
            if (hexOverlay.Visible)
                throw new InvalidOperationException("Fallout optional hex overlay did not return to hidden.");

            var target = Fo1HexMath.Neighbors(loaded.Session.PlayerTile)
                .FirstOrDefault(loaded.Session.CanWalk, -1);
            if (target < 0)
                throw new InvalidOperationException("V13ENT entry has no provisionally walkable adjacent hex.");
            var initialAp = loaded.Session.ActionPoints;
            loaded.Session.SelectTile(target);
            for (var frame = 0; frame < 180 && loaded.Session.PlayerTile != target; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (loaded.Session.PlayerTile != target || loaded.Session.ActionPoints != initialAp - 1 ||
                loaded.Session.PlayerHexCenterErrorMeters > 0.0001f)
                throw new InvalidOperationException(
                    $"Fallout center-to-center one-AP movement proof failed: " +
                    $"tile={loaded.Session.PlayerTile} AP={loaded.Session.ActionPoints} " +
                    $"centerError={loaded.Session.PlayerHexCenterErrorMeters:F6}.");
            var hostileMarkers = Descendants<MeshInstance3D>(host)
                .Count(node => node.Name == "HostileHexMarker");
            var hostileLabels = Descendants<Label3D>(host)
                .Count(node => node.Name == "HostileHealthLabel");
            var combatTarget = loaded.Session.CycleTarget()
                ?? throw new InvalidOperationException("Fallout target cycling found no source mob.");
            if (ownedCreaturePresentation &&
                (MathF.Abs(combatTarget.CreatureUnitsToMeters - 0.0142875f) > 0.000001f ||
                 MathF.Abs(combatTarget.CreatureSelectionMultiplier - 1.35f) > 0.0001f ||
                 combatTarget.CreatureGroundErrorMeters > 0.0001f ||
                 !combatTarget.HostileMarkerDepthTested))
                throw new InvalidOperationException(
                    $"Fallout creature selection scale drift: units={combatTarget.CreatureUnitsToMeters:F7} " +
                    $"selection={combatTarget.CreatureSelectionMultiplier:F4} " +
                    $"groundError={combatTarget.CreatureGroundErrorMeters:F6} " +
                    $"markerDepth={combatTarget.HostileMarkerDepthTested}");
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, combatTarget.Tile);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var cutawayCandidates = loaded.CaveCutaway.Candidates;
            var cutawayHidden = loaded.CaveCutaway.HiddenInstances;
            var meltMaterials = loaded.CaveCutaway.MeltMaterials;
            var visibleHostileMarkers = loaded.Session.VisibleHostileMarkers;
            var visibleHostileBeacons = loaded.Session.VisibleHostileBeacons;
            var visibleHostileLabels = loaded.Session.VisibleHostileLabels;
            if (ownedCavePresentation &&
                (cutawayCandidates < loaded.RuntimeProfile.Cutaway.MinimumCandidateInstances ||
                 cutawayHidden < 1 ||
                 meltMaterials < cutawayCandidates || !loaded.CaveCutaway.ShaderDriven))
                throw new InvalidOperationException(
                    $"Fallout cave melt proof failed: candidates={cutawayCandidates} " +
                    $"occluders={cutawayHidden} materials={meltMaterials} " +
                    $"shaderDriven={loaded.CaveCutaway.ShaderDriven}");
            var targetReticle = host.FindChild("SelectedTargetReticle", true, false) as Control;
            var targetReticleVisible = targetReticle is not null && targetReticle.Visible;
            if (hostileMarkers != 20 || hostileLabels != 20)
                throw new InvalidOperationException(
                    $"Fallout hostile readability contract failed: markers={hostileMarkers} labels={hostileLabels}");
            if (visibleHostileMarkers >= 20 ||
                visibleHostileBeacons > visibleHostileMarkers ||
                visibleHostileLabels > 1)
                throw new InvalidOperationException(
                    $"Fallout proximity readability failed: markers={visibleHostileMarkers} " +
                    $"beacons={visibleHostileBeacons} labels={visibleHostileLabels}.");
            var targetHpBefore = combatTarget.HitPoints;
            var apBeforeAttack = loaded.Session.ActionPoints;
            var magazineBeforeRanged = loaded.Session.MagazineRounds;
            var rangedAttemptsBefore = loaded.Session.RangedAttacks;
            loaded.Session.ActivateTile(combatTarget.Tile, false);
            var rangedResult = loaded.Session.AttackSelectedRanged();
            if (!rangedResult.Attempted ||
                loaded.Session.RangedAttacks != rangedAttemptsBefore + 1 ||
                loaded.Session.ActionPoints != apBeforeAttack - loaded.Session.WeaponActionPointCost ||
                loaded.Session.MagazineRounds != magazineBeforeRanged - 1)
                throw new InvalidOperationException(
                    "Fallout inventory-backed tactical ranged-attempt proof failed.");
            for (var attempts = 0; combatTarget.Alive && attempts < 8; attempts++)
            {
                if (loaded.Session.ActionPoints < loaded.Session.WeaponActionPointCost)
                {
                    loaded.Session.EndTurn();
                    await WaitForRatTurnPresentation(host, loaded);
                }
                loaded.Session.ActivateTile(combatTarget.Tile, false);
                rangedResult = loaded.Session.AttackSelectedRanged();
                if (!rangedResult.Attempted)
                    throw new InvalidOperationException(
                        "Fallout tactical ranged retry was unexpectedly rejected.");
            }
            if (combatTarget.Alive || loaded.Session.RangedHits < 1 ||
                combatTarget.HitPoints >= targetHpBefore)
                throw new InvalidOperationException(
                    "Fallout deterministic tactical ranged hit/death proof failed.");
            await host.ToSignal(
                host.GetTree().CreateTimer(loaded.RuntimeProfile.Mob.Animation.DeathRollSeconds),
                SceneTreeTimer.SignalName.Timeout);
            if (!combatTarget.CorpseVisible || combatTarget.CorpseGroundErrorMeters > 0.005f)
                throw new InvalidOperationException(
                    $"Fallout defeated-rat grounding failed: visible={combatTarget.CorpseVisible} " +
                    $"error={combatTarget.CorpseGroundErrorMeters:F6}");

            if (loaded.Session.ActionPoints < loaded.RuntimeProfile.Gameplay.ReloadActionPointCost)
            {
                loaded.Session.EndTurn();
                await WaitForRatTurnPresentation(host, loaded);
            }
            var magazineBeforeReload = loaded.Session.MagazineRounds;
            var reserveBeforeReload = loaded.Session.ReserveRounds;
            if (!loaded.Session.Reload() ||
                loaded.Session.MagazineRounds <= magazineBeforeReload ||
                loaded.Session.ReserveRounds >= reserveBeforeReload ||
                loaded.Session.Reloads != 1)
                throw new InvalidOperationException(
                    "Fallout source-capacity tactical reload proof failed.");

            var meleeTarget = loaded.Session.Mobs
                .Where(mob => mob.Alive)
                .OrderBy(mob => Fo1HexMath.Distance(loaded.Session.PlayerTile, mob.Tile))
                .ThenBy(mob => mob.Serial)
                .First();
            await MoveAdjacentToTarget(host, loaded, meleeTarget, maximumTurns: 18);
            var meleeHitsBefore = loaded.Session.MeleeHits;
            Fo1TacticalSession.CombatResult meleeResult = default;
            for (var attempts = 0; meleeTarget.Alive && attempts < 8; attempts++)
            {
                if (Fo1HexMath.Distance(loaded.Session.PlayerTile, meleeTarget.Tile) > 1)
                    await MoveAdjacentToTarget(host, loaded, meleeTarget, maximumTurns: 8);
                if (loaded.Session.ActionPoints < loaded.Session.MeleeActionPointCost)
                {
                    loaded.Session.EndTurn();
                    await WaitForRatTurnPresentation(host, loaded);
                }
                loaded.Session.ActivateTile(meleeTarget.Tile, false);
                meleeResult = loaded.Session.AttackSelectedMelee();
                if (!meleeResult.Attempted)
                    throw new InvalidOperationException(
                        "Fallout tactical knife attack was unexpectedly rejected.");
                if (!meleeResult.Hit)
                {
                    loaded.Session.EndTurn();
                    await WaitForRatTurnPresentation(host, loaded);
                }
            }
            if (meleeTarget.Alive || !meleeResult.Hit ||
                loaded.Session.MeleeHits <= meleeHitsBefore ||
                loaded.Session.EquippedWeaponSymbol != "PID_KNIFE" ||
                heldWeapon.Root.Visible || !heldMeleeWeapon.Root.Visible)
                throw new InvalidOperationException(
                    "Fallout tactical melee kill/persistent equipped-knife proof failed.");

            var fpsMeleeTarget = meleeTarget.Alive
                ? meleeTarget
                : loaded.Session.Mobs
                    .Where(mob => mob.Alive)
                    .OrderBy(mob => Fo1HexMath.Distance(loaded.Session.PlayerTile, mob.Tile))
                    .ThenBy(mob => mob.Serial)
                    .First();
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, fpsMeleeTarget.Tile) > 1)
                await MoveAdjacentToTarget(host, loaded, fpsMeleeTarget, maximumTurns: 18);
            camera.SetFirstPersonMode(true);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var fpsMeleeAim = fpsMeleeTarget.GlobalPosition +
                Vector3.Up * loaded.RuntimeProfile.Gameplay.FirstPersonTargetHeightMeters -
                camera.FirstPersonEyePosition;
            var fpsMeleeHitsBefore = loaded.Session.MeleeHits;
            var fpsMeleeSucceeded = false;
            for (var attempts = 0; fpsMeleeTarget.Alive && attempts < 8; attempts++)
            {
                if (attempts > 0)
                    await host.ToSignal(
                        host.GetTree().CreateTimer(
                            loaded.RuntimeProfile.Gameplay.FirstPersonMeleeCooldownSeconds),
                        SceneTreeTimer.SignalName.Timeout);
                fpsMeleeAim = fpsMeleeTarget.GlobalPosition +
                    Vector3.Up * loaded.RuntimeProfile.Gameplay.FirstPersonTargetHeightMeters -
                    camera.FirstPersonEyePosition;
                fpsMeleeSucceeded = loaded.Session.MeleeFirstPerson(
                    camera.FirstPersonEyePosition,
                    fpsMeleeAim.Normalized());
                if (!fpsMeleeSucceeded)
                    break;
            }
            if (!fpsMeleeSucceeded || fpsMeleeTarget.Alive ||
                loaded.Session.MeleeHits <= fpsMeleeHitsBefore)
                throw new InvalidOperationException(
                    $"Fallout continuous FPS melee kill proof failed: success={fpsMeleeSucceeded} " +
                    $"alive={fpsMeleeTarget.Alive} " +
                    $"tileDistance={Fo1HexMath.Distance(loaded.Session.PlayerTile, fpsMeleeTarget.Tile)} " +
                    $"aimMeters={fpsMeleeAim.Length():F6} " +
                    $"reachMeters={loaded.RuntimeProfile.Gameplay.FirstPersonMeleeReachMeters:F6} " +
                    $"cooldown={loaded.Session.FirstPersonMeleeCooldownSeconds:F6} " +
                    $"status={loaded.Session.Status}.");
            camera.SetExplorationMode(false);

            loaded.Session.SwapEquippedWeapon();
            if (loaded.Session.EquippedWeaponSymbol != "PID_10MM_PISTOL" ||
                !heldWeapon.Root.Visible || heldMeleeWeapon.Root.Visible)
                throw new InvalidOperationException(
                    "Fallout equipped-weapon swap to pistol did not update the 3D presentation.");
            loaded.Session.SwapEquippedWeapon();
            if (loaded.Session.EquippedWeaponSymbol != "PID_KNIFE" ||
                heldWeapon.Root.Visible || !heldMeleeWeapon.Root.Visible)
                throw new InvalidOperationException(
                    "Fallout equipped-weapon swap round trip did not restore the knife.");

            var combatPresentation = loaded.Session.CombatPresentation
                ?? throw new InvalidOperationException(
                    "Fallout owned combat presentation was not attached.");
            if (combatPresentation.Tracers != loaded.Session.RangedAttacks ||
                combatPresentation.Impacts != combatPresentation.Tracers ||
                combatPresentation.Casings != combatPresentation.Tracers ||
                combatPresentation.GroundedCasings != combatPresentation.Casings ||
                combatPresentation.Ricochets < 1 ||
                combatPresentation.MeleeSweeps < 2 ||
                combatPresentation.AudioEvents < combatPresentation.Tracers * 2)
                throw new InvalidOperationException(
                    $"Fallout combat-effects proof failed: tracers={combatPresentation.Tracers} " +
                    $"ranged={loaded.Session.RangedAttacks} impacts={combatPresentation.Impacts} " +
                    $"casings={combatPresentation.Casings} ricochets={combatPresentation.Ricochets} " +
                    $"melee={combatPresentation.MeleeSweeps} audio={combatPresentation.AudioEvents}.");

            var turnBeforeEndGate = loaded.Session.Turn;
            loaded.Session.EndTurn();
            if (loaded.Session.Turn != turnBeforeEndGate + 1 ||
                loaded.Session.ActionPoints != initialAp ||
                loaded.Session.RatActivationDistanceHexes != 6 ||
                loaded.Session.LastRatActors >= loaded.CombatMobs)
                throw new InvalidOperationException("Fallout end-turn AP restoration proof failed.");

            var report = new
            {
                schema = "opennv-fo1-tactical-proof/v1",
                status = "pass",
                sceneSha256 = loaded.SceneSha256,
                runtimeProfile = loaded.RuntimeProfile.Report(),
                grid = new
                {
                    width = Fo1HexMath.Width,
                    height = Fo1HexMath.Height,
                    flatToFlatMeters = Fo1HexMath.FlatToFlatMeters,
                    layout = "fallout-even-column-offset-flat-v1",
                },
                entryTile = loaded.EntryTile,
                movedToTile = target,
                moveDistanceMeters = Fo1HexMath.Distance(loaded.EntryTile, target),
                movementCostAp = loaded.RuntimeProfile.Gameplay.TacticalMoveActionPointCost,
                movementDestinationCenterErrorMeters = loaded.Session.PlayerHexCenterErrorMeters,
                combat = new
                {
                    targetSerial = combatTarget.Serial,
                    targetPid = combatTarget.Pid,
                    targetSourceHitPoints = targetHpBefore,
                    targetRemainingHitPoints = combatTarget.HitPoints,
                    targetSourceActionPoints = combatTarget.MaximumActionPoints,
                    targetSourceArmorClass = combatTarget.ArmorClass,
                    targetSourceMeleeDamage = combatTarget.MeleeDamage,
                    targetSourceSequence = combatTarget.Sequence,
                    targetSourceTeam = combatTarget.Team,
                    targetSourceAiPacket = combatTarget.AiPacket,
                    playerWeaponApCost = loaded.Session.WeaponActionPointCost,
                    playerMeleeApCost = loaded.Session.MeleeActionPointCost,
                    attacks = loaded.Session.Attacks,
                    rangedAttempts = loaded.Session.RangedAttacks,
                    rangedHits = loaded.Session.RangedHits,
                    meleeAttempts = loaded.Session.MeleeAttacks,
                    meleeHits = loaded.Session.MeleeHits,
                    reloads = loaded.Session.Reloads,
                    magazineRounds = loaded.Session.MagazineRounds,
                    reserveRounds = loaded.Session.ReserveRounds,
                    kills = loaded.Session.Kills,
                    playerHitPointsAfterRatTurn = loaded.Session.PlayerHitPoints,
                    hostileMarkers,
                    hostileHealthLabels = hostileLabels,
                    proximityVisibleMarkers = visibleHostileMarkers,
                    proximityVisibleBeacons = visibleHostileBeacons,
                    proximityVisibleLabels = visibleHostileLabels,
                    worldNameLabelsSuppressed = visibleHostileLabels <= 1,
                    targetCycleAndFrame = true,
                    screenTargetReticle = targetReticleVisible,
                    creatureUnitsToMeters = combatTarget.CreatureUnitsToMeters,
                    creatureSelectionMultiplier = combatTarget.CreatureSelectionMultiplier,
                    creatureGroundErrorMeters = combatTarget.CreatureGroundErrorMeters,
                    hostileMarkerDepthTested = combatTarget.HostileMarkerDepthTested,
                    corpseVisible = combatTarget.CorpseVisible,
                    corpseGroundErrorMeters = combatTarget.CorpseGroundErrorMeters,
                    localActivationDistanceHexes = loaded.Session.RatActivationDistanceHexes,
                    locallyActiveRatsOnTurn = loaded.Session.LastRatActors,
                    wholeCaveAggroPrevented = loaded.Session.LastRatActors < loaded.CombatMobs,
                    equippedWeaponSymbol = loaded.Session.EquippedWeaponSymbol,
                    weaponSwapRoundTrip = true,
                },
                turnAfterEnd = loaded.Session.Turn,
                actionPointsAfterEnd = loaded.Session.ActionPoints,
                camera = new
                {
                    middleMouseOrbit = true,
                    rightMousePan = true,
                    wheelZoomTowardCursor = true,
                    thirdPersonToggle = true,
                    thirdPersonShoulderTacticalOrbit = true,
                    thirdPersonClickMovementUsesHexCenters = true,
                    firstPersonToggle = true,
                    firstPersonContinuousLocomotion = true,
                    firstPersonMoveDistanceMeters = firstPersonContinuousMoveMeters,
                    firstPersonTacticalActionPointsConsumed = !firstPersonApUnchanged,
                    firstPersonHitscanFire = true,
                    firstPersonMissProofShots = firstPersonMissShots,
                    firstPersonProofShots = loaded.Session.FpsShots,
                    firstPersonProofHits = loaded.Session.FpsHits,
                    firstPersonHitConfirmed,
                    firstPersonHitDamage,
                    firstPersonMeleeConfirmed = true,
                    firstPersonEyeHeightMeters = camera.FirstPersonEyeHeightMeters,
                    firstPersonFovDegrees = camera.FirstPersonFovDegrees,
                    firstPersonEyeErrorMeters,
                    firstPersonForwardAlignment,
                    firstPersonMouseUpLooksUp = true,
                    firstPersonPitchBeforeMouseUpDegrees = Mathf.RadToDeg(firstPersonPitchBeforeMouseUp),
                    firstPersonPitchAfterMouseUpDegrees = Mathf.RadToDeg(firstPersonPitchAfterMouseUp),
                    firstPersonForwardYAfterMouseUp,
                    firstPersonSourceCardsSuppressed = true,
                    firstPersonPlayerSuppressed = true,
                    firstPersonHeldWeaponSuppressed = true,
                    firstPersonHoverSelectorSuppressed = true,
                    selectorHexBasis = "authoritative-flat-top",
                    initialYawDegrees = Mathf.RadToDeg(initialYaw),
                    resultingYawDegrees = Mathf.RadToDeg(camera.TargetYawRadians),
                    initialPitchDegrees = Mathf.RadToDeg(initialPitch),
                    resultingPitchDegrees = Mathf.RadToDeg(camera.TargetPitchRadians),
                    initialSizeMeters = initialSize,
                    resultingSizeMeters = camera.TargetSizeMeters,
                    panDeltaMeters = (camera.Position - initialPosition).Length(),
                },
                sourceSpriteAnchoring = new
                {
                    sprites = sourceSprites.Length,
                    actorSprites = actorSprites.Length,
                    actorBillboard = "fixed-y",
                    staticWorldSprites = staticSprites.Length,
                    staticBillboard = "disabled-world-locked",
                    staticWorldYawDegrees = loaded.RuntimeProfile.Generation.StaticWorldSpriteYawDegrees,
                    groundAnchorY = loaded.RuntimeProfile.Scene.SourceSprites.GroundAnchorMeters,
                    maximumAnchorError,
                    sourceStaticOverlayVisible = sourceOverlay.Visible,
                },
                ownedCreature3d = new
                {
                    enabled = ownedCreaturePresentation,
                    sourceRatSpritesHidden = sourceRatSprites.All(sprite => !sprite.Visible),
                    instances = creatureRoots.Length,
                    meshesPerInstance = loaded.CreatureMeshes,
                    skeletons = creatureSkeletons,
                    animationPlayers = creaturePlayers,
                    importedAnimations = loaded.CreatureAnimations,
                    hiddenIntactStateGoreMeshes = hiddenGoreMeshes,
                },
                ownedPlayer3d = new
                {
                    enabled = ownedPlayerPresentation,
                    sourceSpriteHidden = playerSourceSprite is not null && !playerSourceSprite.Visible,
                    formId = loaded.PlayerActor?.FormId,
                    meshes = loaded.PlayerActor?.Meshes ?? 0,
                    skeletons = playerSkeletons,
                    animationPlayers = playerAnimationPlayers,
                    importedAnimations = loaded.PlayerActor?.Animations ?? 0,
                    playingAnimation = loaded.PlayerActor?.PlayingAnimation,
                    authoredSurfaces = loaded.PlayerActor?.AuthoredSurfaces ?? 0,
                    textures = loaded.PlayerActor?.AuthoredTextures ?? 0,
                    heightMeters = loaded.PlayerActor?.Bounds.Size.Y ?? 0.0f,
                    thirdPersonWeapon = new
                    {
                        role = heldWeapon.Role,
                        formId = heldWeapon.FormId,
                        editorId = heldWeapon.EditorId,
                        sourceSha256 = heldWeapon.SourceSha256,
                        bone = heldWeapon.BoneName,
                        muzzleMarker = heldWeapon.MuzzleMarker,
                        meshes = heldWeapon.Meshes,
                        surfaces = heldWeapon.Surfaces,
                        materialBindings = heldWeapon.MaterialBindings,
                        materialTextures = heldWeapon.MaterialTextures,
                        tacticalVisible = heldWeapon.Root.IsVisibleInTree(),
                        firstPersonSuppressed = true,
                    },
                    thirdPersonMeleeWeapon = new
                    {
                        role = heldMeleeWeapon.Role,
                        formId = heldMeleeWeapon.FormId,
                        gameplayPid = heldMeleeWeapon.GameplayPid,
                        editorId = heldMeleeWeapon.EditorId,
                        sourceSha256 = heldMeleeWeapon.SourceSha256,
                        bone = heldMeleeWeapon.BoneName,
                        meshes = heldMeleeWeapon.Meshes,
                        surfaces = heldMeleeWeapon.Surfaces,
                        materialBindings = heldMeleeWeapon.MaterialBindings,
                        materialTextures = heldMeleeWeapon.MaterialTextures,
                        firstPersonSuppressed = true,
                    },
                },
                combatPresentation = combatPresentation.Report(),
                cave3d = new
                {
                    boundaryEdges = loaded.CaveBoundaryEdges,
                    obstacles = loaded.CaveObstacles,
                    triangles = loaded.CaveTriangles,
                    fixedWorldGeometry = true,
                    defaultVisible = ownedCavePresentation,
                    cutawayCandidates,
                    combatCutawayOccluders = cutawayHidden,
                    meltShaderMaterials = meltMaterials,
                    shaderDrivenCameraMelt = loaded.CaveCutaway.ShaderDriven,
                    atmosphere = new
                    {
                        schema = loaded.Atmosphere.Schema,
                        backgroundColor = new[]
                        {
                            loaded.Atmosphere.BackgroundColor.R,
                            loaded.Atmosphere.BackgroundColor.G,
                            loaded.Atmosphere.BackgroundColor.B,
                        },
                        fogColor = new[]
                        {
                            loaded.Atmosphere.FogColor.R,
                            loaded.Atmosphere.FogColor.G,
                            loaded.Atmosphere.FogColor.B,
                        },
                        depthFogDensity = loaded.Atmosphere.FogDensity,
                        volumetricFogEnabled = loaded.Atmosphere.VolumetricFogEnabled,
                        volumetricFogDensity = loaded.Atmosphere.VolumetricFogDensity,
                        volumetricFogLengthMeters = loaded.Atmosphere.VolumetricFogLengthMeters,
                        practicalLights = loaded.Atmosphere.PracticalLights,
                        directionalLights = loaded.Atmosphere.DirectionalLights,
                        localFogVolumes = loaded.Atmosphere.LocalFogVolumes,
                        tacticalEnvelopeCutHeightMeters =
                            loaded.RuntimeProfile.Cutaway.TacticalEnvelopeCutHeightMeters,
                        lowerEnvelopeBackdropRetained = true,
                    },
                    owned = new
                    {
                        enabled = ownedCavePresentation,
                        manifestSha256 = loaded.OwnedCave.ManifestSha256,
                        assets = loaded.OwnedCave.Assets,
                        instances = loaded.OwnedCave.Instances,
                        meshInstances = loaded.OwnedCave.MeshInstances,
                        surfaceInstances = loaded.OwnedCave.SurfaceInstances,
                        materialBindings = loaded.OwnedCave.MaterialBindings,
                        roles = loaded.OwnedCave.Roles,
                        continuousFloorVisible = continuousFloor?.Visible ?? false,
                        continuousFloorHexes = loaded.OwnedCave.ContinuousFloorHexes,
                        continuousFloorTriangles = loaded.OwnedCave.ContinuousFloorTriangles,
                        continuousFloorMeshInstances = loaded.OwnedCave.ContinuousFloorMeshInstances,
                        embeddedVaultPortalInstances =
                            loaded.OwnedCave.Roles.GetValueOrDefault("vault-portal"),
                        groundedRockInstances = loaded.OwnedCave.GroundedInstances,
                        groundingFloorHeightMeters = loaded.OwnedCave.GroundingFloorHeightMeters,
                        minimumGroundSeatDepthMeters =
                            loaded.OwnedCave.MinimumGroundSeatDepthMeters,
                        maximumGroundSeatDepthMeters =
                            loaded.OwnedCave.MaximumGroundSeatDepthMeters,
                        maximumGroundErrorMeters = loaded.OwnedCave.MaximumGroundErrorMeters,
                        groundingToleranceMeters = loaded.OwnedCave.GroundingToleranceMeters,
                    },
                },
                optionalHexOverlay = new
                {
                    defaultVisible = false,
                    togglePassed = true,
                    depthTested = true,
                    opaque = true,
                    hexes = hexOverlay.GetMeta("hex_count").AsInt32(),
                    uniqueEdges = hexOverlay.GetMeta("edge_count").AsInt32(),
                    presentationFootprintBlockedHexes =
                        hexOverlay.GetMeta("presentation_footprint_blocked_hexes").AsInt32(),
                },
                session = loaded.Session.Report(),
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(
                fullPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO1_TACTICAL_PROOF_PASS moved={loaded.EntryTile}->{target} ap=1 " +
                $"targetPid={combatTarget.Pid} attacks={loaded.Session.Attacks} kills={loaded.Session.Kills}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_TACTICAL_PROOF_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task MoveAdjacentToTarget(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1Mob target,
        int maximumTurns)
    {
        for (var turn = 0; turn < maximumTurns; turn++)
        {
            if (!target.Alive)
                throw new InvalidOperationException(
                    "Fallout melee approach target died before the attack proof.");
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, target.Tile) <= 1)
                return;
            if (loaded.Session.ActionPoints == 0)
            {
                loaded.Session.EndTurn();
                await WaitForRatTurnPresentation(host, loaded);
            }
            var destination = FindReachableAdjacentTile(loaded.Session, target);
            loaded.Session.SelectTile(destination);
            for (var frame = 0; loaded.Session.QueuedMovementSteps > 0 && frame < 480; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (loaded.Session.QueuedMovementSteps > 0)
                throw new InvalidOperationException(
                    "Fallout melee approach did not finish its queued center-hex movement.");
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, target.Tile) <= 1)
                return;
            loaded.Session.EndTurn();
            await WaitForRatTurnPresentation(host, loaded);
        }
        throw new InvalidOperationException(
            $"Fallout melee approach exceeded {maximumTurns} normal tactical turns.");
    }

    private static int FindReachableAdjacentTile(Fo1TacticalSession session, Fo1Mob target)
    {
        var occupied = session.Mobs
            .Where(mob => mob.Alive && mob != target)
            .Select(mob => mob.Tile)
            .ToHashSet();
        var goals = Fo1HexMath.Neighbors(target.Tile)
            .Where(tile => session.CanWalk(tile) && !occupied.Contains(tile))
            .ToHashSet();
        if (goals.Count == 0)
            throw new InvalidOperationException(
                "Fallout melee target has no source-walkable adjacent hex.");
        var queue = new Queue<int>();
        var visited = new HashSet<int> { session.PlayerTile };
        queue.Enqueue(session.PlayerTile);
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            if (goals.Contains(tile))
                return tile;
            foreach (var neighbor in Fo1HexMath.Neighbors(tile))
            {
                if (!session.CanWalk(neighbor) || occupied.Contains(neighbor) ||
                    !visited.Add(neighbor))
                    continue;
                queue.Enqueue(neighbor);
            }
        }
        throw new InvalidOperationException(
            "Fallout melee target has no source-walkable approach path.");
    }

    private static async Task WaitForRatTurnPresentation(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        await host.ToSignal(
            host.GetTree().CreateTimer(loaded.RuntimeProfile.Mob.Animation.MoveSeconds),
            SceneTreeTimer.SignalName.Timeout);
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

}
