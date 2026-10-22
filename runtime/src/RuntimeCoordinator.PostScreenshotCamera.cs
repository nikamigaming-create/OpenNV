using Godot;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    // Queries use the resident native collision world. No actor, gameplay body,
    // replacement geometry or movement command is created for a screenshot.
    private sealed record PostScreenshotCameraCheck(bool Passed, string Reason, Vector3 Foot,
        Vector3 Camera, Dictionary<string, object?> Evidence);

    private static float[] PostScreenshotCoordinates(Vector3 value) => [value.X, value.Y, value.Z];

    private PostScreenshotCameraCheck CheckPostScreenshotCamera(PhysicsDirectSpaceState3D space,
        Vector3 intendedFoot, Vector3 requestedCamera, Basis uprightBasis, bool groundCamera,
        float fov, float near, float aspect)
    {
        var player = _configuration.Player;
        var eyeOffset = uprightBasis * (Vector3.Up * player.SpawnCenterHeightMeters +
            new Vector3(player.DesktopCameraOffsetMeters[0], player.DesktopCameraOffsetMeters[1], player.DesktopCameraOffsetMeters[2]));
        var evidence = new Dictionary<string, object?>
        {
            ["scope"] = "resident native collision only; missing/collision-less visual geometry remains unverified",
            ["intendedFootMeters"] = PostScreenshotCoordinates(intendedFoot),
            ["requestedCameraMeters"] = PostScreenshotCoordinates(requestedCamera),
            ["configuredEyeOffsetMeters"] = PostScreenshotCoordinates(eyeOffset),
            ["configuredEyeHeightMeters"] = eyeOffset.Y,
            ["capsuleRadiusMeters"] = player.CapsuleRadiusMeters,
            ["capsuleHeightMeters"] = player.CapsuleHeightMeters,
            ["capsuleCenterHeightMeters"] = player.SpawnCenterHeightMeters,
            ["collisionMask"] = player.CollisionMask,
            ["maximumWalkableSlopeDegrees"] = player.MaximumWalkableSlopeDegrees,
            ["floorSnapMeters"] = player.CapsuleRadiusMeters,
            ["floorToleranceMeters"] = _configuration.Proof.SpawnFloorToleranceMeters,
            ["physicsFrame"] = Engine.GetPhysicsFrames(),
            ["proxyActorCreated"] = false,
        };
        var foot = intendedFoot; var camera = requestedCamera;
        PostScreenshotCameraCheck Finish(string reason)
        {
            evidence["passed"] = reason == "clear"; evidence["reason"] = reason;
            evidence["resolvedFootMeters"] = PostScreenshotCoordinates(foot);
            evidence["resolvedCameraMeters"] = PostScreenshotCoordinates(camera);
            return new(reason == "clear", reason, foot, camera, evidence);
        }
        if (!intendedFoot.IsFinite() || !requestedCamera.IsFinite() ||
            !float.IsFinite(aspect) || aspect <= 0 || !float.IsFinite(near) || near <= 0 ||
            !float.IsFinite(fov) || fov is <= 0 or >= 180)
            return Finish("invalid-camera-query");
        // The first native hit must be the intended nearby floor. Never search
        // past a roof or scan other positions for a more flattering view.
        using var ray = PhysicsRayQueryParameters3D.Create(
            intendedFoot + Vector3.Up * _configuration.Proof.SpawnFloorRayStartMeters,
            intendedFoot + Vector3.Up * _configuration.Proof.SpawnFloorRayEndMeters,
            player.CollisionMask);
        ray.CollideWithAreas = false; ray.CollideWithBodies = true; ray.HitFromInside = true;
        evidence["floorRayFromMeters"] = PostScreenshotCoordinates(ray.From);
        evidence["floorRayToMeters"] = PostScreenshotCoordinates(ray.To);
        using var support = space.IntersectRay(ray);
        if (support.Count == 0) return Finish("native-floor-missing");
        var point = support["position"].AsVector3(); var normal = support["normal"].AsVector3();
        evidence["floorPointMeters"] = PostScreenshotCoordinates(point);
        evidence["floorNormalRaw"] = PostScreenshotCoordinates(normal);
        evidence["floorCollider"] = PostScreenshotContact(support);
        evidence["floorDeltaMeters"] = point.Y - intendedFoot.Y;
        if (!point.IsFinite() || !normal.IsFinite() || normal.LengthSquared() < .000001f)
            return Finish("native-floor-not-walkable");
        normal = normal.Normalized();
        evidence["floorNormal"] = PostScreenshotCoordinates(normal);
        if (normal.Dot(Vector3.Up) < MathF.Cos(Mathf.DegToRad(player.MaximumWalkableSlopeDegrees)))
            return Finish("native-floor-not-walkable");
        if (MathF.Abs(point.Y - intendedFoot.Y) > _configuration.Proof.SpawnFloorToleranceMeters)
            return Finish("native-floor-outside-intended-height");
        if (groundCamera)
        {
            // The root ray touches a sloped plane below the capsule's bottom
            // pole. An upright lower hemisphere needs r / nY vertical center
            // separation, rather than r, to touch that plane without overlap.
            // Derive this support lift from the observed normalized normal;
            // the complete native capsule query below must still be clear.
            var precision = MathF.BitIncrement(MathF.Abs(point.Y)) - MathF.Abs(point.Y);
            var numericLift = MathF.Max(_configuration.Proof.PortalAlignmentToleranceMeters, precision * 4);
            var slopeLift = MathF.Max(0, player.CapsuleRadiusMeters * (1 / normal.Y - 1));
            var lift = slopeLift + numericLift;
            evidence["numericClearanceLiftMeters"] = numericLift;
            evidence["floorSlopeSupportLiftMeters"] = slopeLift;
            evidence["totalFloorSupportLiftMeters"] = lift;
            if (!float.IsFinite(lift) || lift >= player.CapsuleRadiusMeters)
                return Finish("native-floor-support-lift-exceeds-snap");
            foot = point + Vector3.Up * lift;
            camera = foot + eyeOffset;
        }
        // Same root support/slope/snap rules as NativeCharacterStep.TrySupport,
        // expressed as a pure query because there is no gameplay body to move.
        using var rootRay = PhysicsRayQueryParameters3D.Create(foot + Vector3.Up * _configuration.Proof.PortalAlignmentToleranceMeters,
            foot - Vector3.Up * MathF.Min(player.StepHeightMeters, player.CapsuleRadiusMeters), player.CollisionMask);
        rootRay.CollideWithAreas = false; rootRay.CollideWithBodies = true;
        using var rootSupport = space.IntersectRay(rootRay);
        if (rootSupport.Count == 0 || rootSupport["normal"].AsVector3().Dot(Vector3.Up) <
            MathF.Cos(Mathf.DegToRad(player.MaximumWalkableSlopeDegrees))) return Finish("ordinary-root-support-missing");
        using var capsule = new CapsuleShape3D { Radius = player.CapsuleRadiusMeters, Height = player.CapsuleHeightMeters };
        using var capsuleQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = capsule,
            Transform = new(uprightBasis, foot + uprightBasis * Vector3.Up * player.SpawnCenterHeightMeters),
            CollisionMask = player.CollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Margin = 0,
        };
        var capsuleHits = PostScreenshotContacts(space, capsuleQuery);
        evidence["capsuleContacts"] = capsuleHits;
        if (capsuleHits.Length != 0) return Finish("ordinary-capsule-obstructed");
        // Enclose the whole near plane, including its corners, about the eye.
        var halfHeight = near * MathF.Tan(Mathf.DegToRad(fov) * .5f);
        var sphereRadius = MathF.Sqrt(near * near + halfHeight * halfHeight * (1 + aspect * aspect));
        evidence["cameraSphereRadiusMeters"] = sphereRadius;
        using var sphere = new SphereShape3D { Radius = sphereRadius };
        using var cameraQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = sphere,
            Transform = new(Basis.Identity, camera),
            CollisionMask = player.CollisionMask,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Margin = 0,
        };
        var cameraHits = PostScreenshotContacts(space, cameraQuery);
        evidence["cameraContacts"] = cameraHits;
        return Finish(cameraHits.Length == 0 ? "clear" : "camera-near-plane-obstructed");
    }

    private static Dictionary<string, object?> PostScreenshotContact(Godot.Collections.Dictionary hit)
    {
        var node = hit.TryGetValue("collider", out var collider) ? collider.AsGodotObject() as Node : null;
        string? reference = null;
        for (var owner = node; owner is not null; owner = owner.GetParent())
            if (owner.HasMeta("opennv_reference_form_key")) { reference = owner.GetMeta("opennv_reference_form_key").AsString(); break; }
        return new()
        {
            ["colliderPath"] = node?.GetPath().ToString(),
            ["sourceReference"] = reference,
            ["colliderId"] = hit.TryGetValue("collider_id", out var id) ? id.AsInt64().ToString() : node?.GetInstanceId().ToString(),
            ["shape"] = hit.TryGetValue("shape", out var shape) ? shape.AsInt32() : null,
        };
    }

    private static Dictionary<string, object?>[] PostScreenshotContacts(PhysicsDirectSpaceState3D space,
        PhysicsShapeQueryParameters3D query)
    {
        var contacts = space.IntersectShape(query, 16);
        return contacts.Select(hit => { using (hit) return PostScreenshotContact(hit); }).ToArray();
    }

    private async Task RunPostScreenshotCameraContracts(string output)
    {
        var root = new Node3D { Name = "PostScreenshotCameraNeutralContracts" };
        var rows = new List<object>();
        try
        {
            AddChild(root);
            void Box(string name, Vector3 position, Vector3 size, float slope = 0)
            {
                var body = new StaticBody3D { Name = name, Position = position, CollisionLayer = _configuration.Player.CollisionMask };
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
                body.RotateZ(Mathf.DegToRad(slope)); root.AddChild(body);
            }
            Box("Floor", new(0, -.5f, 0), new(20, 1, 20));
            Box("Wall", new(3, 1.5f, 0), new(.2f, 3, 4));
            Box("LowCeiling", new(-3, 1.4f, 0), new(2, .2f, 2));
            Box("FrontWall", new(0, 1.5f, -1), new(2, 3, .2f));
            Box("SteepFloor", new(0, .8f, 7), new(4, .2f, 2), 70);
            // Put the plane at Y=0 at each test point. These are isolated
            // native boxes, outside the flat-floor control's footprint.
            Box("ShallowFloor", new(20, -.1f / MathF.Cos(Mathf.DegToRad(2.8f)), 0), new(8, .2f, 8), 2.8f);
            Box("ModerateFloor", new(40, -.1f / MathF.Cos(Mathf.DegToRad(30)), 0), new(8, .2f, 8), 30);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var eye = Vector3.Up * _configuration.Player.SpawnCenterHeightMeters +
                new Vector3(_configuration.Player.DesktopCameraOffsetMeters[0], _configuration.Player.DesktopCameraOffsetMeters[1], _configuration.Player.DesktopCameraOffsetMeters[2]);
            PostScreenshotCameraCheck Check(string name, Vector3 foot, string expected, float near = .02f)
            {
                var result = CheckPostScreenshotCamera(root.GetWorld3D().DirectSpaceState, foot, foot + eye, Basis.Identity, true, 90, near, 16f / 9f);
                rows.Add(new { name, expected, actual = result.Reason, result.Evidence });
                if (result.Reason != expected) throw new InvalidOperationException($"Camera contract {name}: expected {expected}, observed {result.Reason}.");
                return result;
            }
            Check("clear-standing", Vector3.Zero, "clear");
            Check("wall", new(2.7f, 0, 0), "ordinary-capsule-obstructed");
            Check("low-ceiling", new(-3, 0, 0), "native-floor-outside-intended-height");
            Check("missing-floor", new(30, 0, 0), "native-floor-missing");
            Check("steep-floor", new(0, .8f, 7), "native-floor-not-walkable");
            Check("near-plane-wall", Vector3.Zero, "camera-near-plane-obstructed", .5f);
            var shallow = Check("walkable-2.8-degree-support", new(20, 0, 0), "clear");
            var moderate = Check("walkable-30-degree-support", new(40, 0, 0), "clear");
            foreach (var slope in new[] { shallow, moderate })
            {
                var normalized = (float[])slope.Evidence["floorNormal"]!;
                var observedLift = (float)slope.Evidence["floorSlopeSupportLiftMeters"]!;
                var expectedLift = _configuration.Player.CapsuleRadiusMeters * (1 / normalized[1] - 1);
                if (observedLift <= 0 || !Mathf.IsEqualApprox(observedLift, expectedLift))
                    throw new InvalidOperationException("Walkable-slope support did not retain its observed capsule-plane lift.");
            }
            // The same moderate slope remains obstructed without repositioning
            // the capsule to its derived support height. No floor contacts are
            // excluded from the full query to make the positive cases pass.
            var unliftedFoot = new Vector3(40, _configuration.Proof.PortalAlignmentToleranceMeters, 0);
            var unlifted = CheckPostScreenshotCamera(root.GetWorld3D().DirectSpaceState,
                unliftedFoot, unliftedFoot + eye, Basis.Identity, false, 90, .02f, 16f / 9f);
            rows.Add(new { name = "unlifted-moderate-slope-refused", actual = unlifted.Reason, unlifted.Evidence });
            if (unlifted.Reason != "ordinary-capsule-obstructed")
                throw new InvalidOperationException("The strict capsule query did not reject the unlifted slope contact.");
            var exactCamera = new Vector3(30, 1.62f, 0);
            var exact = CheckPostScreenshotCamera(root.GetWorld3D().DirectSpaceState,
                new(30, 0, 0), exactCamera, Basis.Identity, false, 65, .02f, 16f / 9f);
            rows.Add(new { name = "invalid-reference-pose-preserved", actual = exact.Reason, exact.Evidence });
            if (exact.Passed || exact.Camera != exactCamera || exact.Foot != new Vector3(30, 0, 0))
                throw new InvalidOperationException("A failed exact reference pose was silently repositioned.");
            WriteReport(Path.Combine(output, "camera-contracts.json"), new { passed = true, rows, renderedActors = 0, sourceAssetsUsed = false });
            GD.Print("OPENNV_POST_SCREENSHOT_CAMERA_CONTRACT_PASS clear=true wallRefused=true ceilingRefused=true floorRequired=true slopeRequired=true shallowSlopeSupported=true moderateSlopeSupported=true unliftedSlopeRefused=true nearPlaneRefused=true proxyActors=false");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            WriteReport(Path.Combine(output, "camera-contracts.json"), new { passed = false, error = error.Message, rows });
            GD.PushError($"OPENNV_POST_SCREENSHOT_CAMERA_CONTRACT_FAIL {error}"); GetTree().Quit(1);
        }
        finally { root.Free(); }
    }
}
