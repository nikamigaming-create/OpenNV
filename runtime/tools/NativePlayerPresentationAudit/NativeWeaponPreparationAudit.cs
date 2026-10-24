using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditWeaponPreparation(FalloutPluginStack records, FalloutNativeCampaignState saved)
    {
        foreach (var form in new uint[] { 0x1735d4, 0x8f21e })
        {
            var weapon = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(form));
            var inventory = new FalloutPlayerInventory();
            inventory.Add(records, weapon.Form, 1, 1, true);
            inventory.Add(records, weapon.Ammunition[0], 120, 1, true);
            inventory.Equip(records, weapon.Form);
            var player = new RuntimeNativePlayer();
            player.Configure(RuntimeConfiguration.Load(), Transform3D.Identity);
            AddChild(player); player.SetPhysicsProcess(false);
            using var world = new FalloutReferenceWorld(records);
            var package = new RuntimeNativePlayerPackage(records, player, new FalloutScriptSession(), world, () => saved.ActiveCell);
            try
            {
                var appearance = FalloutNpcAppearanceResolver.Resolve(records, records.RuntimeFormKey(7),
                    appearanceState: FalloutNativeCharacterCreation.ActorState(records, records.RuntimeFormKey(7), saved.Character));
                player.ConfigurePresentation(records, inventory, () => appearance, () => Colors.White);
                var handling = JsonSerializer.Serialize(player.CaptureWeaponHandling());
                var audio = JsonSerializer.Serialize(package.CaptureAudio());
                var items = JsonSerializer.Serialize(inventory.Items);
                for (var frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await CheckAimPreparation(player, records);
                CheckArmSpread(records, FalloutWeaponShot.Read(records, weapon.Form, weapon.Ammunition[0]));
                CheckLaunchSpread(records, FalloutWeaponShot.Read(records, weapon.Form, weapon.Ammunition[0]));
                var state = JsonSerializer.SerializeToElement(player.FiringState);
                if (state.GetProperty("resourcePreparation").GetProperty("error").ValueKind != JsonValueKind.Null ||
                    state.GetProperty("shots").GetInt64() != 0 ||
                    records.SoundVoices.ActiveVoices != 0 ||
                    state.GetProperty("muzzle").GetProperty("visible").GetBoolean() ||
                    state.GetProperty("muzzle").GetProperty("particles").EnumerateArray().Any(particle => particle.GetProperty("BirthCount").GetInt64() != 0) ||
                    JsonSerializer.Serialize(player.CaptureWeaponHandling()) != handling ||
                    JsonSerializer.Serialize(package.CaptureAudio()) != audio ||
                    JsonSerializer.Serialize(inventory.Items) != items)
                    throw new InvalidOperationException("Resource preparation dispatched gameplay/audio, changed randomness/ammunition, or failed its source binding.");
                GD.Print($"OPENNV_WEAPON_PREPARATION_PASS weapon={weapon.Form} shots=0 muzzleHidden=true births=0 " +
                    "handlingAndRandomUnchanged=true inventoryUnchanged=true audioHistoryUnchanged=true nativeVoices=0 gameplay=separate");
            }
            finally { player.Free(); }
        }
    }

    private async Task CheckAimPreparation(RuntimeNativePlayer player, FalloutPluginStack records)
    {
        await CheckDirectObjectAim(player);
        var shape = player.AutoAimShape(20, 3);
        var identity = shape.GetRid();
        var points = shape.Points;
        for (var shot = 0; shot < 128; shot++)
            if (player.AutoAimShape(20, 3).GetRid() != identity || !shape.Points.SequenceEqual(points))
                throw new InvalidOperationException("Repeated aim queries rebuilt or changed their native hull.");
        var changed = player.AutoAimShape(30, 4);
        if (changed.GetRid() == identity || changed.Points.Skip(16).Any(point => point.Z != -30))
            throw new InvalidOperationException("A changed aim declaration reused stale geometry.");
        var invalid = false;
        try { _ = player.AutoAimShape(20, float.NaN); }
        catch (InvalidDataException) { invalid = true; }
        if (!invalid || player.AutoAimShape(20, 3).GetRid() != identity)
            throw new InvalidOperationException("Invalid aim settings changed retained geometry.");

        player.SetProcess(false); player.SetPhysicsProcess(false);
        var body = player.FindChildren("*", "", true, false).OfType<RuntimeNativePlayerActor>()
            .Single(actor => actor.BodyContacts.Count != 0);
        var original = body.GlobalTransform;
        var target = player.CombatTargetPoint;
        var offset = new Vector3(.35f, -.4f, .2f);
        try
        {
            body.GlobalPosition += offset;
            if (player.CombatTargetPoint.DistanceTo(target + offset) > .0001f)
                throw new InvalidOperationException("Player targeting ignored the posed body displacement.");
            var camera = player.Camera.GlobalTransform;
            player.Camera.GlobalPosition += Vector3.Right * 2;
            if (player.CombatTargetPoint.DistanceTo(target + offset) > .0001f)
                throw new InvalidOperationException("View camera displaced the anatomical damage target.");
            player.Camera.GlobalTransform = camera;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (player.CombatHitPartFrom(player.CombatTargetPoint + Vector3.Forward * 3) is null)
                throw new InvalidOperationException("A clear attack ray to the posed body missed its damage volumes.");
        }
        finally { body.GlobalTransform = original; }
        GD.Print("OPENNV_SHARED_AIM_PREPARATION_PASS retainedHull=true changedSettings=true invalidAtomic=true displacedBodyTarget=true cameraIndependent=true anatomicalRayHit=true");
    }

    private async Task CheckDirectObjectAim(RuntimeNativePlayer player)
    {
        var reference = new Node3D { Position = new Vector3(100, 10, 100) };
        reference.SetMeta("opennv_reference_form_key", "AimFixture.esm:000001");
        var body = new StaticBody3D { CollisionLayer = player.CollisionMask | player.CollisionLayer };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .035f, Height = .2f } });
        reference.AddChild(body); AddChild(reference);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var origin = reference.GlobalPosition + Vector3.Back * 3;
            // A malformed assist cone would throw if assistance were consulted.
            // A direct reference contact needs no actor query in either mode.
            var point = player.ResolveShotAimPoint(origin, Vector3.Forward, 20, 20, float.NaN);
            if (point is null || point.Value.DistanceTo(reference.GlobalPosition + Vector3.Back * .035f) > .002f)
                throw new InvalidOperationException("A directly aimed small object lost its native contact to actor assistance.");
            if (player.ResolveShotAimPoint(origin + Vector3.Right, Vector3.Forward, 20, 0, 0) is not null)
                throw new InvalidOperationException("An empty aim ray invented an object contact.");
            GD.Print("OPENNV_DIRECT_OBJECT_AIM_PASS nativeRay=true smallTarget=true parentReference=true actorAssistBypassed=true sharedFlatXr=true fixture=synthetic");
        }
        finally { reference.Free(); }
    }

    private static void CheckArmSpread(FalloutPluginStack records, FalloutWeaponShot source)
    {
        var owner = new FalloutWeaponSpread(records);
        foreach (var (type, setting) in new[] { (3u, "fCrippledArms1HSpreadPenalty"), (5u, "fCrippledArms2HSpreadPenalty") })
        {
            var shot = source with { WeaponAnimationType = type, StrengthRequirement = 0, SkillRequirement = 0 };
            float Spread(bool left, bool right) => owner.PlayerMedianDeviationDegrees(shot, 100, 10,
                aiming: true, moving: false, running: false, sneaking: false, left, right, [], _ => 0);
            var multiplier = 1 - FalloutGameSettingFloats.Read(records, "fWobbleToSkillConversion");
            var expectedDifference = FalloutGameSettingFloats.Read(records, setting) * multiplier * 5.955f;
            if (MathF.Abs(Spread(true, true) - Spread(false, false) - expectedDifference) > .0001f)
                throw new InvalidOperationException("Both-arm spread stacked the single-arm penalty.");
            if (type == 3 && Spread(true, false) != Spread(false, false))
                throw new InvalidOperationException("The free left arm penalized a one-handed weapon.");
        }
        GD.Print("OPENNV_SHARED_ARM_SPREAD_PASS oneHanded=true twoHanded=true exclusiveInjuryStates=true");
    }

    private static void CheckLaunchSpread(FalloutPluginStack records, FalloutWeaponShot source)
    {
        var shot = source with
        {
            MinimumSpread = 3,
            Spread = 5,
            WeaponAnimationType = 3,
            StrengthRequirement = 0,
            SkillRequirement = 0,
            AmmoEffects = []
        };
        var owner = new FalloutWeaponSpread(records);
        var process = FalloutActorProjectileLaunch.NewProcess();
        var penalty = Math.Max(FalloutGameSettingFloats.Read(records, "fMinGunSpreadValue"),
            (1 - FalloutGameSettingFloats.Read(records, "fWobbleToSkillConversion") * .3f) *
            FalloutGameSettingFloats.Read(records, "fStandingSpreadPenalty"));
        var expected = Mathf.DegToRad(3 + penalty * FalloutGameSettingFloats.Read(records, "fNPCMaxGunWobbleAngle"));
        float Extent(FalloutActorProjectileLaunch state) => owner.NpcLaunchExtentRadians(shot, 30, 2,
            false, false, false, false, false, state, []);
        if (MathF.Abs(Extent(process) - expected) > 1e-6f ||
            MathF.Abs(Extent(new(2, -.01f)) - expected - Mathf.DegToRad(10) + .01f) > 1e-6f)
            throw new InvalidOperationException("Launch radius merged independent process and actor components.");
        var retained = new FalloutActorProjectileLaunch(2, -.2f);
        var cold = JsonSerializer.Deserialize<FalloutActorProjectileLaunch>(JsonSerializer.Serialize(retained))!;
        if (cold != retained || MathF.Abs(cold.Advance(.1, 3, 1).AimChaseRadians + .14f) > 1e-6f ||
            new FalloutActorProjectileLaunch(0, .2f).Advance(1, 3, 1).AimChaseRadians != 0 ||
            new FalloutActorProjectileLaunch(0, 1e-7f).Advance(0, 3, 1).AimChaseRadians != 0)
            throw new InvalidOperationException("Aim-chase cold state, signed decay or crossing clamp changed.");
        var calls = 0;
        var center = new Vector3(.3f, .2f, -.8f).Normalized();
        var zero = FalloutWeaponSpread.DeviateLaunch(center, 0, () => { calls++; return .5f; });
        if (calls != 2 || zero.DistanceTo(center) > 1e-6f)
            throw new InvalidOperationException("Zero launch extent skipped source draws or changed heading.");
        var samples = new Queue<float>([1, .25f]);
        var vertical = FalloutWeaponSpread.DeviateLaunch(Vector3.Up, .1f, samples.Dequeue);
        if (MathF.Abs(vertical.Y - MathF.Cos(.1f)) > 1e-6f ||
            MathF.Abs(vertical.X + MathF.Sin(.1f)) > 1e-6f || samples.Count != 0)
            throw new InvalidOperationException("Vertical source heading did not survive Euler perturbation.");
        for (var i = 0; i < 128; i++)
        {
            var values = new Queue<float>([1, i / 128f]);
            var result = FalloutWeaponSpread.DeviateLaunch(center, -.2f, values.Dequeue);
            if (center.AngleTo(result) > .20001f || MathF.Abs(result.Length() - 1) > 1e-6f)
                throw new InvalidOperationException("Euler launch escaped its conservative friendly-query extent.");
        }
        GD.Print("OPENNV_PROJECTILE_LAUNCH_CONTRACT_PASS processCold=true signedDecay=true sourceEuler=true zeroDraws=2 friendExtent=true bitParity=false");
    }

}
