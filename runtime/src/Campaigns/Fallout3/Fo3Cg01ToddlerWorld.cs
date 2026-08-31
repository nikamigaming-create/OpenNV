using System.Text.Json;
using Godot;


using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg01ToddlerWorldContract(
    string CellFormId,
    string PlayerStartMarkerFormId,
    Fo3Cg01Transform PlayerStartTransform,
    float PlayerScale,
    float VerticalFovDegrees,
    float NearGameUnits,
    float SpawnCenterHeightMeters,
    float CapsuleRadiusMeters,
    float CapsuleHeightMeters,
    float MoveSpeedMetersPerSecond,
    float MouseSensitivityRadiansPerPixel,
    float VerticalLookLimitRadians,
    Vector3 DesktopCameraOffsetMeters,
    float CameraFarMeters,
    uint CollisionLayer,
    uint CollisionMask,
    float GravityMetersPerSecondSquared,
    string MoveLeftAction,
    string MoveRightAction,
    string MoveForwardAction,
    string MoveBackwardAction,
    string ActivateAction,
    float ActivationDistanceMeters,
    string TriggerReferenceFormId,
    int TargetStage)
{
    internal const string ExpectedSchema = "opennv-fo3-cg01-toddler-world/v1";
    internal const string ExpectedSavedStateSchema =
        "opennv-fo3-cg01-toddler-world-state/v1";

    private const string ExpectedStatus =
        "source-marker-camera-and-open-nv-physics-policy-runtime-ready";
    private const string ExpectedPhysicsAuthority =
        "open-nv-player-policy-scaled-by-owned-player-scale";
    private const string ExpectedPlayerRole = "player";
    private const int VectorDimensions = 3;
    private const float MinimumValue = 0.0f;
    private const float PerspectiveMaximumDegrees = 180.0f;
    private const float FalloutReferenceAspectHeightOverWidth = 0.75f;
    private const float FovDiameterToRadius = 0.5f;
    private const string ExpectedGodotKeepAspect = "keep-height";

    internal static Fo3Cg01ToddlerWorldContract Load(
        JsonElement source,
        Fo3Cg01Stage0Transition stage0,
        Fo3Cg01Stage12Transition stage12,
        RuntimeConfiguration configuration)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            !RequiredBoolean(source, "runtimeReady") ||
            source.GetProperty("blocker").ValueKind != JsonValueKind.Null ||
            RequiredFormId(source, "cellFormId") != stage0.CellFormId ||
            RequiredFormId(source, "triggerReferenceFormId") !=
                stage12.Trigger.ReferenceFormId ||
            RequiredInteger(source, "targetStage") != stage12.TargetStage)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler-world identity differs.");

        var player = RequiredObject(source, "player");
        var marker = RequiredObject(player, "startMarker");
        var markerTransform = LoadTransform(RequiredObject(marker, "sourceTransform"));
        var playerScale = RequiredPositiveSingle(player, "scale");
        if (RequiredString(player, "role") != ExpectedPlayerRole ||
            RequiredBoolean(player, "visualBodyPrepared") ||
            RequiredFormId(marker, "formId") != stage0.PlayerStartMarker.FormId ||
            RequiredFormId(marker, "cellFormId") != stage0.CellFormId ||
            !SameTransform(markerTransform, stage0.PlayerStartMarker.SourceTransform))
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler player marker differs.");

        var camera = RequiredObject(source, "camera");
        var sourceHorizontalFov = RequiredPositiveSingle(
            camera,
            "sourceHorizontalFovDegrees");
        var referenceAspect = RequiredPositiveSingle(
            camera,
            "referenceAspectHeightOverWidth");
        var verticalFov = RequiredPositiveSingle(camera, "verticalFovDegrees");
        var nearGameUnits = RequiredPositiveSingle(camera, "nearGameUnits");
        var expectedVerticalFov = Mathf.RadToDeg(
            2.0f * Mathf.Atan(
                Mathf.Tan(Mathf.DegToRad(sourceHorizontalFov) * FovDiameterToRadius) *
                referenceAspect));
        if (sourceHorizontalFov >= PerspectiveMaximumDegrees ||
            !Mathf.IsEqualApprox(
                referenceAspect,
                FalloutReferenceAspectHeightOverWidth) ||
            !Mathf.IsEqualApprox(verticalFov, expectedVerticalFov) ||
            RequiredString(camera, "godotKeepAspect") != ExpectedGodotKeepAspect)
            throw new InvalidOperationException("Fallout 3 CG01 toddler camera differs.");

        var physics = RequiredObject(source, "physicsPolicy");
        if (RequiredString(physics, "authority") != ExpectedPhysicsAuthority)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler physics authority differs.");
        configuration.VerifyCompiledConfigurationDescriptor(
            RequiredObject(physics, "runtimeConfiguration"));
        VerifyRuntimePolicy(physics, configuration);

        return new Fo3Cg01ToddlerWorldContract(
            stage0.CellFormId,
            stage0.PlayerStartMarker.FormId,
            markerTransform,
            playerScale,
            verticalFov,
            nearGameUnits,
            configuration.Player.SpawnCenterHeightMeters * playerScale,
            configuration.Player.CapsuleRadiusMeters * playerScale,
            configuration.Player.CapsuleHeightMeters * playerScale,
            configuration.Player.MoveSpeedMetersPerSecond * playerScale,
            configuration.Player.MouseSensitivityRadiansPerPixel,
            configuration.Player.VerticalLookLimitRadians,
            configuration.Player.DesktopCameraOffsetMeters.Vector3() * playerScale,
            configuration.Player.CameraFarMeters,
            configuration.Player.CollisionLayer,
            configuration.Player.CollisionMask,
            configuration.Simulation.GravityMetersPerSecondSquared,
            configuration.Player.DesktopInput.MoveLeft.Action,
            configuration.Player.DesktopInput.MoveRight.Action,
            configuration.Player.DesktopInput.MoveForward.Action,
            configuration.Player.DesktopInput.MoveBackward.Action,
            configuration.Player.DesktopInput.Activate.Action,
            configuration.Player.ActivationDistanceMeters,
            stage12.Trigger.ReferenceFormId,
            stage12.TargetStage);
    }

    internal object SavedState(Fo3Cg01ToddlerWorldState state) => new
    {
        schema = ExpectedSavedStateSchema,
        cellFormId = state.CellFormId,
        playerStartMarkerFormId = state.PlayerStartMarkerFormId,
        playerPositionMeters = Vector(state.PlayerPositionMeters),
        playerRotation = Quaternion(state.PlayerRotation),
        triggerReferenceFormId = state.TriggerReferenceFormId,
        triggerEntered = state.TriggerEntered,
        movementEnabled = state.MovementEnabled,
        authoredCollisionBodies = state.AuthoredCollisionBodies,
        visualBodyPrepared = false,
    };

    internal Fo3Cg01ToddlerWorldState LoadSavedState(JsonElement source)
    {
        if (RequiredString(source, "schema") != ExpectedSavedStateSchema ||
            RequiredFormId(source, "cellFormId") != CellFormId ||
            RequiredFormId(source, "playerStartMarkerFormId") != PlayerStartMarkerFormId ||
            RequiredFormId(source, "triggerReferenceFormId") != TriggerReferenceFormId ||
            !RequiredBoolean(source, "triggerEntered") ||
            RequiredBoolean(source, "visualBodyPrepared"))
            throw new InvalidOperationException(
                "Saved Fallout 3 CG01 toddler world differs.");
        var state = new Fo3Cg01ToddlerWorldState(
            CellFormId,
            PlayerStartMarkerFormId,
            ReadVector(source, "playerPositionMeters"),
            ReadQuaternion(source, "playerRotation"),
            TriggerReferenceFormId,
            true,
            RequiredBoolean(source, "movementEnabled"),
            RequiredPositiveInteger(source, "authoredCollisionBodies"));
        return state;
    }

    private static void VerifyRuntimePolicy(
        JsonElement source,
        RuntimeConfiguration configuration)
    {
        var player = configuration.Player;
        var simulation = configuration.Simulation;
        if (!Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "spawnCenterHeightMeters"),
                player.SpawnCenterHeightMeters) ||
            !Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "capsuleRadiusMeters"),
                player.CapsuleRadiusMeters) ||
            !Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "capsuleHeightMeters"),
                player.CapsuleHeightMeters) ||
            !Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "moveSpeedMetersPerSecond"),
                player.MoveSpeedMetersPerSecond) ||
            !Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "mouseSensitivityRadiansPerPixel"),
                player.MouseSensitivityRadiansPerPixel) ||
            !Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "verticalLookLimitRadians"),
                player.VerticalLookLimitRadians) ||
            !ReadVector(source, "desktopCameraOffsetMeters").IsEqualApprox(
                player.DesktopCameraOffsetMeters.Vector3()) ||
            !Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "cameraFarMeters"),
                player.CameraFarMeters) ||
            (uint)RequiredPositiveInteger(source, "collisionLayer") != player.CollisionLayer ||
            (uint)RequiredPositiveInteger(source, "collisionMask") != player.CollisionMask ||
            !Mathf.IsEqualApprox(
                RequiredPositiveSingle(source, "gravityMetersPerSecondSquared"),
                simulation.GravityMetersPerSecondSquared))
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler physics policy differs from the runtime.");

        var input = RequiredObject(source, "desktopInput");
        VerifyInput(input, "moveLeft", player.DesktopInput.MoveLeft);
        VerifyInput(input, "moveRight", player.DesktopInput.MoveRight);
        VerifyInput(input, "moveForward", player.DesktopInput.MoveForward);
        VerifyInput(input, "moveBackward", player.DesktopInput.MoveBackward);
        VerifyInput(input, "activate", player.DesktopInput.Activate);
    }

    private static void VerifyInput(
        JsonElement source,
        string name,
        DesktopKeyBindingConfiguration expected)
    {
        var binding = RequiredObject(source, name);
        if (RequiredString(binding, "action") != expected.Action ||
            RequiredString(binding, "physicalKey") != expected.PhysicalKey)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler input differs: {name}");
    }

    private static bool SameTransform(Fo3Cg01Transform left, Fo3Cg01Transform right) =>
        SameVector(left.PositionGameUnits, right.PositionGameUnits) &&
        SameVector(left.RotationRadians, right.RotationRadians) &&
        Math.Abs(left.Scale - right.Scale) <= double.Epsilon;

    private static bool SameVector(Fo3Cg01Vector3 left, Fo3Cg01Vector3 right) =>
        Math.Abs(left.X - right.X) <= double.Epsilon &&
        Math.Abs(left.Y - right.Y) <= double.Epsilon &&
        Math.Abs(left.Z - right.Z) <= double.Epsilon;

    private static Fo3Cg01Transform LoadTransform(JsonElement source) => new(
        ReadVector3(source, "positionGameUnits"),
        ReadVector3(source, "rotationRadians"),
        RequiredPositiveDouble(source, "scale"));

    private static Fo3Cg01Vector3 ReadVector3(JsonElement source, string name)
    {
        var value = ReadVector(source, name);
        return new Fo3Cg01Vector3(value.X, value.Y, value.Z);
    }

    private static Vector3 ReadVector(JsonElement source, string name)
    {
        var values = RequiredArray(source, name).EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (values.Length != VectorDimensions || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler vector differs: {name}");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement source, string name)
    {
        var values = RequiredArray(source, name).EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (values.Length != VectorDimensions + 1 ||
            values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler quaternion differs: {name}");
        var result = new Quaternion(values[0], values[1], values[2], values[3]);
        if (!result.IsNormalized())
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler quaternion differs: {name}");
        return result;
    }

    private static float RequiredPositiveSingle(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetSingle(out var result) ||
            !float.IsFinite(result) || result <= MinimumValue)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
        return result;
    }

    private static double RequiredPositiveDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result) || result <= MinimumValue)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
        return result;
    }

    private static int RequiredPositiveInteger(JsonElement parent, string name)
    {
        var result = RequiredInteger(parent, name);
        return result > 0
            ? result
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler FormID {name} is invalid.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
        return value.GetString()!;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                $"Fallout 3 CG01 toddler field {name} is invalid.");
        return value;
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];
    private static float[] Quaternion(Quaternion value) => [value.X, value.Y, value.Z, value.W];
}

internal sealed record Fo3Cg01ToddlerWorldState(
    string CellFormId,
    string PlayerStartMarkerFormId,
    Vector3 PlayerPositionMeters,
    Quaternion PlayerRotation,
    string TriggerReferenceFormId,
    bool TriggerEntered,
    bool MovementEnabled,
    int AuthoredCollisionBodies);

internal sealed record Fo3Cg01ToddlerWorldRuntime(
    Fo3Cg01ToddlerWorldContract Contract,
    Fo3Cg01ToddlerPlayer Player,
    Area3D DadTrigger,
    int AuthoredCollisionBodies)
{
    private const float TriggerRotationToleranceRadians = 0.00001f;

    internal static Fo3Cg01ToddlerWorldRuntime Build(
        Node3D host,
        Fo3Vault101BirthSceneCoverage scene,
        Fo3Cg01ToddlerWorldContract contract,
        Fo3Cg01Stage0State stage5,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12Transition stage12,
        Fo3Cg01ToddlerWorldState? restored,
        Action<Fo3Cg01ToddlerPlayer> entered)
    {
        if (stage5.ActiveStage != stage10.SourceStage ||
            stage10.ActiveStage != stage12.SourceStage ||
            stage5.Player.MoveTargetFormId != contract.PlayerStartMarkerFormId ||
            !Mathf.IsEqualApprox((float)stage5.Player.Scale, contract.PlayerScale) ||
            stage12.Trigger.ReferenceFormId != contract.TriggerReferenceFormId ||
            stage12.TargetStage != contract.TargetStage ||
            scene.Contract.CellFormId != contract.CellFormId ||
            scene.AuthoredCollisionBodies <= 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler runtime joins differ.");

        scene.Camera.Current = false;
        var triggerTransform = stage12.Trigger.SourceTransform;
        if (Math.Abs(triggerTransform.RotationRadians.X) > TriggerRotationToleranceRadians ||
            Math.Abs(triggerTransform.RotationRadians.Y) > TriggerRotationToleranceRadians)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler trigger tilt is unsupported.");
        var trigger = new Area3D
        {
            Name = $"REFR_{stage12.Trigger.ReferenceFormId}_CG01_DAD_TRIGGER",
            Position = SourceLocalPosition(
                triggerTransform.PositionGameUnits,
                scene.Contract.EntryPositionGameUnits),
            Rotation = new Vector3(
                MinimumValue,
                -(float)triggerTransform.RotationRadians.Z,
                MinimumValue),
            CollisionLayer = 0,
            CollisionMask = contract.CollisionLayer,
            Monitoring = restored is null,
            Monitorable = false,
        };
        trigger.SetMeta("opennv_source_form_id", stage12.Trigger.ReferenceFormId);
        trigger.SetMeta("opennv_source_collision_layers", stage12.Trigger.CollisionLayers);
        trigger.AddChild(new CollisionShape3D
        {
            Name = "OWNED_XPRM_BOX",
            Shape = new BoxShape3D
            {
                Size = new Vector3(
                    (float)stage12.Trigger.DimensionsGameUnits.X,
                    (float)stage12.Trigger.DimensionsGameUnits.Z,
                    (float)stage12.Trigger.DimensionsGameUnits.Y),
            },
        });
        scene.CellRoot.AddChild(trigger);

        var player = new Fo3Cg01ToddlerPlayer();
        host.AddChild(player);
        player.Configure(contract, scene.Contract, restored);
        trigger.BodyEntered += body =>
        {
            if (body != player)
                return;
            player.StopAtAuthoredTrigger();
            entered(player);
        };
        return new Fo3Cg01ToddlerWorldRuntime(
            contract,
            player,
            trigger,
            scene.AuthoredCollisionBodies);
    }

    internal Fo3Cg01ToddlerWorldState State(bool triggerEntered) => new(
        Contract.CellFormId,
        Contract.PlayerStartMarkerFormId,
        Player.GlobalPosition,
        Player.GlobalBasis.GetRotationQuaternion(),
        Contract.TriggerReferenceFormId,
        triggerEntered,
        Player.MovementEnabled,
        AuthoredCollisionBodies);

    internal Area3D InstallStage20Interactions(
        Fo3Vault101BirthSceneCoverage scene,
        Fo3Cg01Stage20Interaction interaction,
        Action gateActivated,
        Action exitEntered,
        Action bookActivated)
    {
        Player.ConfigureSourceActivation(interaction, gateActivated, bookActivated);
        var source = interaction.ExitTriggerTransform;
        var trigger = new Area3D
        {
            Name = $"REFR_{interaction.ExitTriggerReferenceFormId}_CG01_EXIT_CRIB_TRIGGER",
            Position = SourceLocalPosition(source.PositionGameUnits, scene.Contract.EntryPositionGameUnits),
            Rotation = new Vector3(0, -(float)source.RotationRadians.Z, 0),
            CollisionLayer = 0,
            CollisionMask = Contract.CollisionLayer,
            Monitoring = true,
            Monitorable = false,
        };
        trigger.SetMeta("opennv_source_form_id", interaction.ExitTriggerReferenceFormId);
        trigger.AddChild(new CollisionShape3D
        {
            Name = "OWNED_XPRM_BOX",
            Shape = new BoxShape3D
            {
                Size = new Vector3(
                (float)interaction.ExitTriggerDimensionsGameUnits.X,
                (float)interaction.ExitTriggerDimensionsGameUnits.Z,
                (float)interaction.ExitTriggerDimensionsGameUnits.Y)
            },
        });
        trigger.BodyEntered += body =>
        {
            if (body != Player || !trigger.Monitoring)
                return;
            trigger.Monitoring = false;
            exitEntered();
        };
        scene.CellRoot.AddChild(trigger);
        return trigger;
    }

    private const float MinimumValue = 0.0f;

    private static Vector3 SourceLocalPosition(
        Fo3Cg01Vector3 source,
        Vector3 origin) =>
        GamebryoCoordinate.ConvertVector(
            new Vector3((float)source.X, (float)source.Y, (float)source.Z) - origin);
}

internal sealed partial class Fo3Cg01ToddlerPlayer : CharacterBody3D
{
    private const float MinimumValue = 0.0f;
    private const float PressedInputStrength = 1.0f;

    private Fo3Cg01ToddlerWorldContract _contract = null!;
    private Camera3D _camera = null!;
    private float _pitch;
    private bool _acceptanceTracking;
    private bool _acceptanceInputPressed;
    private Fo3Cg01Stage20Interaction? _interaction;
    private Action? _gateActivated;
    private Action? _bookActivated;
    private Func<InputEvent, bool>? _menuInputHandler;

    internal bool MovementEnabled { get; private set; } = true;
    internal int AcceptancePhysicsFrames { get; private set; }
    internal float AcceptanceHorizontalTravelMeters { get; private set; }

    internal void Configure(
        Fo3Cg01ToddlerWorldContract contract,
        Fo3Vault101BirthPresentationContract scene,
        Fo3Cg01ToddlerWorldState? restored)
    {
        _contract = contract;
        Name = "CG01_TODDLER_PLAYER_PHYSICAL_BODY";
        CollisionLayer = contract.CollisionLayer;
        CollisionMask = contract.CollisionMask;
        MotionMode = MotionModeEnum.Grounded;
        FloorSnapLength = contract.CapsuleRadiusMeters;
        AddChild(new CollisionShape3D
        {
            Name = "OPENNV_POLICY_CAPSULE_SCALED_BY_OWNED_PLAYER_SCALE",
            Shape = new CapsuleShape3D
            {
                Radius = contract.CapsuleRadiusMeters,
                Height = contract.CapsuleHeightMeters,
            },
        });
        _camera = new Camera3D
        {
            Name = "CG01_OWNED_INI_FIRST_PERSON_CAMERA",
            Position = contract.DesktopCameraOffsetMeters,
            Fov = contract.VerticalFovDegrees,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Near = contract.NearGameUnits * scene.UnitsToMeters,
            Far = contract.CameraFarMeters,
            Current = true,
        };
        AddChild(_camera);

        if (restored is null)
        {
            var source = contract.PlayerStartTransform;
            var local = GamebryoCoordinate.ConvertVector(
                new Vector3(
                    (float)source.PositionGameUnits.X,
                    (float)source.PositionGameUnits.Y,
                    (float)source.PositionGameUnits.Z) - scene.EntryPositionGameUnits);
            GlobalPosition = scene.UnitsToMeters * local +
                Vector3.Up * contract.SpawnCenterHeightMeters;
            Rotation = new Vector3(
                MinimumValue,
                -(float)source.RotationRadians.Z,
                MinimumValue);
        }
        else
        {
            GlobalPosition = restored.PlayerPositionMeters;
            GlobalTransform = new Transform3D(
                new Basis(restored.PlayerRotation),
                restored.PlayerPositionMeters);
            MovementEnabled = restored.MovementEnabled;
        }
        SetMeta("opennv_source_player_scale", contract.PlayerScale);
        SetMeta("opennv_visual_body_prepared", false);
    }

    internal void BeginConfiguredInputAcceptance()
    {
        if (!MovementEnabled || _acceptanceTracking || _acceptanceInputPressed)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler configured-input acceptance state differs.");
        _acceptanceTracking = true;
        _acceptanceInputPressed = true;
        Input.ParseInputEvent(new InputEventAction
        {
            Action = _contract.MoveForwardAction,
            Pressed = true,
            Strength = PressedInputStrength,
        });
    }

    internal void StopAtAuthoredTrigger()
    {
        MovementEnabled = false;
        ReleaseAcceptanceInput();
        Velocity = Vector3.Zero;
    }

    internal void CancelConfiguredInputAcceptance()
    {
        _acceptanceTracking = false;
        ReleaseAcceptanceInput();
        Velocity = Vector3.Zero;
    }

    internal void EnableMovementAtSourceStage()
    {
        if (MovementEnabled)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler movement was already enabled.");
        MovementEnabled = true;
    }

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_menuInputHandler?.Invoke(inputEvent) == true)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!MovementEnabled)
            return;
        if (_interaction is not null && inputEvent.IsActionPressed(_contract.ActivateAction))
        {
            var query = PhysicsRayQueryParameters3D.Create(
                _camera.GlobalPosition,
                _camera.GlobalPosition + -_camera.GlobalBasis.Z * _contract.ActivationDistanceMeters,
                _contract.CollisionMask);
            query.Exclude = [GetRid()];
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (hit.TryGetValue("collider", out var value) && value.AsGodotObject() is Node node)
            {
                for (Node? current = node; current is not null; current = current.GetParent())
                {
                    if (!current.HasMeta("opennv_source_form_id"))
                        continue;
                    var formId = current.GetMeta("opennv_source_form_id").AsString();
                    if (formId.Equals(_interaction.GateReferenceFormId, StringComparison.OrdinalIgnoreCase))
                        _gateActivated?.Invoke();
                    else if (formId.Equals(_interaction.BookReferenceFormId, StringComparison.OrdinalIgnoreCase))
                        _bookActivated?.Invoke();
                    break;
                }
            }
            return;
        }
        if (inputEvent is not InputEventMouseMotion motion)
            return;
        Rotation = new Vector3(
            MinimumValue,
            Rotation.Y - motion.Relative.X * _contract.MouseSensitivityRadiansPerPixel,
            MinimumValue);
        _pitch = Mathf.Clamp(
            _pitch - motion.Relative.Y * _contract.MouseSensitivityRadiansPerPixel,
            -_contract.VerticalLookLimitRadians,
            _contract.VerticalLookLimitRadians);
        _camera.Rotation = new Vector3(_pitch, MinimumValue, MinimumValue);
    }

    internal void ConfigureSourceActivation(
        Fo3Cg01Stage20Interaction interaction,
        Action gateActivated,
        Action bookActivated)
    {
        _interaction = interaction;
        _gateActivated = gateActivated;
        _bookActivated = bookActivated;
    }

    internal void SetMenuInputHandler(Func<InputEvent, bool>? handler)
    {
        if ((handler is null) == (_menuInputHandler is null))
            throw new InvalidOperationException(
                "Fallout 3 toddler menu-input lifecycle differs.");
        _menuInputHandler = handler;
        MovementEnabled = handler is null;
        Velocity = Vector3.Zero;
    }

    public override void _PhysicsProcess(double delta)
    {
        var input = Input.GetVector(
            _contract.MoveLeftAction,
            _contract.MoveRightAction,
            _contract.MoveForwardAction,
            _contract.MoveBackwardAction);
        var direction = (GlobalBasis *
            new Vector3(input.X, MinimumValue, input.Y)).Normalized();
        var velocity = MovementEnabled
            ? direction * _contract.MoveSpeedMetersPerSecond
            : Vector3.Zero;
        velocity.Y = IsOnFloor()
            ? MathF.Min(Velocity.Y, MinimumValue)
            : Velocity.Y - _contract.GravityMetersPerSecondSquared * (float)delta;
        Velocity = velocity;
        if (MovementEnabled)
        {
            var before = GlobalPosition;
            MoveAndSlide();
            if (_acceptanceTracking)
            {
                AcceptancePhysicsFrames++;
                AcceptanceHorizontalTravelMeters +=
                    Horizontal(GlobalPosition - before).Length();
            }
        }
    }

    private void ReleaseAcceptanceInput()
    {
        if (!_acceptanceInputPressed)
            return;
        Input.ParseInputEvent(new InputEventAction
        {
            Action = _contract.MoveForwardAction,
            Pressed = false,
            Strength = MinimumValue,
        });
        _acceptanceInputPressed = false;
    }

    private static Vector3 Horizontal(Vector3 value) =>
        new(value.X, MinimumValue, value.Z);
}
