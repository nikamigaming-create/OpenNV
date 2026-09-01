using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private void StartCg02PicturePositioning(
        Fo3Cg02PictureRuntime picture,
        Fo3Cg01DadLeadSequence sharedTravel,
        Fo3Cg01ToddlerPlayer player,
        CellActorLoader.PlacedActor dad,
        CellActorLoader.PlacedActor jonas,
        Func<IReadOnlyCollection<string>> completedPackages,
        Action<string> packageCompleted,
        Action playerReady)
    {
        if (_cg02PicturePackageTick is not null)
            return;
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG02 picture world is absent.");
        var actors = new Dictionary<string, CellActorLoader.PlacedActor>(
            StringComparer.OrdinalIgnoreCase)
        {
            [picture.Packages[0].ActorReferenceFormId] =
                picture.Packages[0].ActorReferenceFormId.Equals(
                    dad.ReferenceFormId, StringComparison.OrdinalIgnoreCase) ? dad : jonas,
            [picture.Packages[1].ActorReferenceFormId] =
                picture.Packages[1].ActorReferenceFormId.Equals(
                    dad.ReferenceFormId, StringComparison.OrdinalIgnoreCase) ? dad : jonas,
        };
        foreach (var package in picture.Packages.Where(value =>
            completedPackages().Contains(value.FormId,
                StringComparer.OrdinalIgnoreCase)))
        {
            var actor = actors[package.ActorReferenceFormId];
            var source = package.TargetTransform;
            var target = GamebryoPackagePlacement.FromPlanarGameReferenceMarker(
                package.TargetMarkerFormId,
                new Vector3((float)source.PositionGameUnits.X,
                    (float)source.PositionGameUnits.Y,
                    (float)source.PositionGameUnits.Z),
                new Vector3((float)source.RotationRadians.X,
                    (float)source.RotationRadians.Y,
                    (float)source.RotationRadians.Z),
                (float)source.Scale,
                coverage.Contract.EntryPositionGameUnits);
            GamebryoPackageTravel.ArriveAtSourceTarget(
                package.FormId, target, actor.Placement.Transform,
                GamebryoPackageTravel.ExactArrivalToleranceCellUnits)
                .Publish(actor.Placement);
            actor.Placement.SetMeta("opennv_picture_ready", 1);
        }
        var travels = picture.Packages.Where(package =>
            !completedPackages().Contains(package.FormId,
                StringComparer.OrdinalIgnoreCase)).Select(package =>
        {
            var actor = actors[package.ActorReferenceFormId];
            var source = package.TargetTransform;
            var target = GamebryoPackagePlacement.FromPlanarGameReferenceMarker(
                package.TargetMarkerFormId,
                new Vector3((float)source.PositionGameUnits.X,
                    (float)source.PositionGameUnits.Y,
                    (float)source.PositionGameUnits.Z),
                new Vector3((float)source.RotationRadians.X,
                    (float)source.RotationRadians.Y,
                    (float)source.RotationRadians.Z),
                (float)source.Scale,
                coverage.Contract.EntryPositionGameUnits);
            var travel = GamebryoPackageTravel.Start(
                package.FormId, target, actor.Placement.Transform,
                [target.SourceTransform.Origin],
                sharedTravel.LocomotionSpeedGameUnitsPerSecond,
                package.RadiusGameUnits);
            var locomotion = actor.Actor.LoadedAnimations.Single(value =>
                ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                    ActorModelSlice.NormalizeAnimationPath(
                        sharedTravel.LocomotionLogicalPath),
                    StringComparison.OrdinalIgnoreCase) &&
                value.SourceSha256.Equals(sharedTravel.LocomotionSha256,
                    StringComparison.OrdinalIgnoreCase));
            locomotion.Player.Play(locomotion.RuntimeName);
            travel.Publish(actor.Placement);
            return (Package: package, Actor: actor, Travel: travel);
        }).ToList();

        var activeTriggerReferences = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        void TryPlayerReady()
        {
            if (activeTriggerReferences.Count == 0 || picture.Packages.Any(package =>
                    !completedPackages().Contains(package.FormId,
                        StringComparer.OrdinalIgnoreCase)))
                return;
            var towardJonas = jonas.Placement.GlobalPosition - player.GlobalPosition;
            towardJonas.Y = 0.0f;
            var forward = -player.GlobalBasis.Z;
            forward.Y = 0.0f;
            if (towardJonas.IsZeroApprox() || forward.IsZeroApprox())
                return;
            var heading = Mathf.RadToDeg(forward.Normalized().SignedAngleTo(
                towardJonas.Normalized(), Vector3.Up));
            if (heading >= picture.MinimumHeadingDegrees &&
                heading <= picture.MaximumHeadingDegrees)
                playerReady();
        }
        foreach (var triggerSource in picture.Triggers)
        {
            var name = $"SOURCE_TRIGGER_{triggerSource.ReferenceFormId}";
            if (coverage.CellRoot.HasNode(name))
                continue;
            var source = triggerSource.SourceTransform;
            var trigger = new Area3D
            {
                Name = name,
                Position = GamebryoCoordinate.ConvertVector(
                    new Vector3((float)source.PositionGameUnits.X,
                        (float)source.PositionGameUnits.Y,
                        (float)source.PositionGameUnits.Z) -
                    coverage.Contract.EntryPositionGameUnits),
                Rotation = new Vector3(0.0f, -(float)source.RotationRadians.Z, 0.0f),
                Scale = Vector3.One * (float)source.Scale,
                CollisionLayer = 0,
                CollisionMask = player.SourceBodyCollisionLayer,
                Monitoring = true,
            };
            trigger.SetMeta("opennv_source_form_id", triggerSource.ReferenceFormId);
            trigger.AddChild(new CollisionShape3D
            {
                Shape = new BoxShape3D
                {
                    Size = new Vector3(
                        (float)triggerSource.DimensionsGameUnits.X,
                        (float)triggerSource.DimensionsGameUnits.Z,
                        (float)triggerSource.DimensionsGameUnits.Y),
                },
            });
            trigger.BodyEntered += body =>
            {
                if (body != player)
                    return;
                activeTriggerReferences.Add(triggerSource.ReferenceFormId);
                TryPlayerReady();
            };
            trigger.BodyExited += body =>
            {
                if (body == player)
                    activeTriggerReferences.Remove(triggerSource.ReferenceFormId);
            };
            coverage.CellRoot.AddChild(trigger);
        }

        if (travels.Count == 0)
            return;
        _cg02PicturePackageTick = delta =>
        {
            foreach (var item in travels.ToArray())
            {
                var arrived = item.Travel.Advance(delta);
                item.Travel.Publish(item.Actor.Placement);
                if (!arrived)
                    continue;
                travels.Remove(item);
                packageCompleted(item.Package.FormId);
                TryPlayerReady();
            }
            if (travels.Count == 0)
                _cg02PicturePackageTick = null;
        };
    }
}
