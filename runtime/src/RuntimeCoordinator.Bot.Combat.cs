using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.Bots;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using NumericVector = System.Numerics.Vector3;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private static readonly JsonSerializerOptions BotCombatJson = new(JsonSerializerDefaults.Web);
    private FalloutPluginStack? _botCombatStack;
    private string? _botCombatSource;
    private string? _botCombatScene;
    private readonly HashSet<FalloutFormKey> _botCombatObservedThreats = [];
    private (FalloutFormKey Weapon, FalloutFormKey Ammunition)? _botCombatWeaponIdentity;
    private FalloutWeaponPresentation? _botCombatWeaponSource;
    private FalloutWeaponShot? _botCombatShotSource;

    private BotSkillBinding NativeBotSkillBinding(RuntimeNativePlayer player)
    {
        if (!ReferenceEquals(_botCombatStack, _nativePluginStack))
        {
            _botCombatStack = _nativePluginStack;
            var sources = RuntimeLiveContentSource.Current?.SaveCompatibilityId + "\n" +
                string.Join('\n', _nativePluginStack?.Plugins.Select(plugin => plugin.Plugin.Name + ":" + plugin.Sha256) ?? []);
            _botCombatSource = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sources)));
            _botCombatWeaponIdentity = null; _botCombatWeaponSource = null; _botCombatShotSource = null;
            _botCombatObservedThreats.Clear();
        }
        return new(typeof(ReactiveCombatSkill).Module.ModuleVersionId.ToString("D"), _botCombatSource ?? "unbound",
            _nativeXr is null ? "flat" : Diagnostics.Parity.RuntimeSimulatorBotInput.DirectoryPath is not null ? "simulator" : "physical-xr",
            $"{System.Environment.ProcessId}:{player.GetInstanceId()}");
    }

    private BotCombatObservation ObserveNativeBotCombat()
    {
        var player = _nativePlayer ?? throw new InvalidOperationException("No native player owner for ordinary bot observation.");
        var scene = _nativeActiveCell?.Cell.FormKey.ToString() ?? "loading";
        if (_botCombatScene != scene)
        { _botCombatScene = scene; _botCombatObservedThreats.Clear(); }
        var world = _nativeReferences;
        var binding = NativeBotSkillBinding(player);
        var controls = player.SourceControls;
        var input = _configuration.Player.DesktopInput;
        var bindings = new BotCombatBindings(input.Fire.Action, RuntimeNativeInputControls.Action(6), input.Reload.Action, input.Cancel.Action);
        var loading = world is null || NativeBotLoading(world);
        var defeated = _nativeDeathPresented || player.IsDefeated?.Invoke() == true;
        var camera = player.Camera.GlobalPosition;
        var forward = -player.Camera.GlobalBasis.Z.Normalized();
        var muzzle = camera; var muzzleForward = forward;
        var vitals = _nativeOpeningStageDriver?.Vitals;
        var fault = _nativeOpeningStageDriver?.BlockingExecutionFault ?? _nativeQuestScripts?.StartupError ??
            world?.PlayerMoves.Error ?? _nativeReferencePresentation?.Error;
        BotCombatWeapon? weapon = null;
        var firing = new BotCombatFiring(0, 0, null, null, null);
        var targets = new List<BotCombatTarget>();
        if (!loading && !defeated)
        {
            try
            {
                var presentation = JsonSerializer.SerializeToElement(player.PresentationState, BotCombatJson);
                fault ??= BotCombatTelemetry.Error(presentation, "error");
                var handling = presentation.GetProperty("weaponHandling");
                firing = BotCombatTelemetry.ReadFiring(handling);
                weapon = ObserveNativeBotWeapon(player, presentation, handling, out muzzle, out muzzleForward);
                var playerKey = _nativePluginStack!.RuntimeFormKey(0x14);
                foreach (var (key, node) in _nativeReferencePresentation!.Nodes)
                {
                    if (node is not (RuntimeNativeNpc or RuntimeNativeCreature) || !GodotObject.IsInstanceValid(node) ||
                        node.IsQueuedForDeletion() || !node.IsInsideTree()) continue;
                    var state = world!.Get(key);
                    var targetsPlayer = !state.Unconscious && !state.Restrained &&
                        world.HasSelectedCombatTarget(key, playerKey);
                    if (!targetsPlayer && !_botCombatObservedThreats.Contains(key)) continue;
                    if (targetsPlayer) _botCombatObservedThreats.Add(key);
                    var resident = node.IsVisibleInTree() && state.Enabled && NativeCollisionResident(node.GlobalPosition);
                    var sourceFault = state.ScriptError ?? state.SelectionFailure?.Error ?? state.PackageBindingFailure?.Error;
                    if (RuntimeNativeActorCombat.Find(node) is not { } owner)
                    {
                        targets.Add(new(key.ToString(), Vector3ToNumeric(node.GlobalPosition), null, null, resident,
                            state.Enabled, targetsPlayer, state.Injury?.Dead == true, null, null, null, null,
                            "Resident source threat has no native combat owner.", sourceFault));
                        continue;
                    }
                    var observation = JsonSerializer.SerializeToElement(owner.Observation, BotCombatJson);
                    var body = observation.GetProperty("bodyTarget");
                    var error = BotCombatTelemetry.Error(observation, "error") ?? BotCombatTelemetry.Error(body, "error") ??
                        owner.EngagementError;
                    NumericVector? point = null;
                    if (body.TryGetProperty("position", out var position))
                    {
                        var values = position.EnumerateArray().Select(value => value.GetSingle()).ToArray();
                        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
                            error ??= "Source BPNT posed body observation is nonfinite or incomplete.";
                        else point = new(values[0], values[1], values[2]);
                    }
                    var dead = owner.Dead;
                    var health = world.Health(key).Current;
                    string? bodyReference = null, muzzleReference = null, obstruction = null;
                    if (resident && !dead && point is { } posed)
                    {
                        var destination = new Vector3(posed.X, posed.Y, posed.Z);
                        (bodyReference, obstruction) = NativeBotCombatRay(player, camera, destination);
                        var muzzleRay = NativeBotCombatRay(player, muzzle, destination);
                        muzzleReference = muzzleRay.Reference;
                        if (muzzleReference != key.ToString()) obstruction = muzzleRay.Collider ?? obstruction;
                    }
                    targets.Add(new(key.ToString(), Vector3ToNumeric(node.GlobalPosition), point,
                        BotCombatTelemetry.Text(body, "owner"), resident, state.Enabled, targetsPlayer, dead, health,
                        bodyReference, muzzleReference, obstruction, error, sourceFault));
                }
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException or InvalidDataException or ArgumentException or KeyNotFoundException or JsonException)
            { fault ??= "Native combat observation owner is unavailable: " + error.Message; }
        }
        string? aimed = null;
        if (!loading && !defeated && weapon is { RangeMeters: > 0 })
        {
            var origin = binding.InputMode == "flat" ? camera : muzzle;
            var direction = binding.InputMode == "flat" ? forward : muzzleForward;
            aimed = NativeBotCombatRay(player, origin, origin + direction * Math.Min(weapon.RangeMeters, ReactiveCombatSkill.MaximumRangeMeters + 1)).Reference;
        }
        return new(checked((long)Engine.GetPhysicsFrames()), scene, binding, Vector3ToNumeric(camera), Vector3ToNumeric(forward),
            Vector3ToNumeric(muzzle), Vector3ToNumeric(muzzleForward), aimed, GetTree().Paused, loading, defeated, player.ModalInput,
            controls.Looking, controls.Fighting, player.CollisionResident, vitals?.ExactHitPoints ?? float.NaN,
            vitals?.MaximumHitPoints ?? float.NaN, bindings, weapon, firing, targets, fault);
    }

    private BotCombatWeapon? ObserveNativeBotWeapon(RuntimeNativePlayer player, JsonElement presentation, JsonElement handling,
        out Vector3 muzzle, out Vector3 muzzleForward)
    {
        muzzle = player.Camera.GlobalPosition; muzzleForward = -player.Camera.GlobalBasis.Z.Normalized();
        var view = presentation.GetProperty(presentation.GetProperty("thirdPerson").GetBoolean() ? "third" : "first");
        if (view.ValueKind != JsonValueKind.Object || BotCombatTelemetry.Text(view, "weapon") is not { } identity) return null;
        var reference = BotSourceReference(identity);
        var source = _botCombatWeaponIdentity?.Weapon == reference && _botCombatWeaponSource is { } cached
            ? cached : FalloutWeaponPresentation.Read(_nativePluginStack!, reference);
        var snapshot = player.CaptureWeaponHandling() ?? throw new InvalidOperationException("Native weapon handling owner is absent.");
        var magazine = snapshot.Magazines.SingleOrDefault(value => value.Weapon == reference);
        var ammo = magazine is { } current
            ? current.Ammunition : source.Ammunition.Where(value => (_nativeInventory.Item(value)?.Count ?? 0) > 0)
                .Select(value => (FalloutFormKey?)value).FirstOrDefault();
        var loaded = magazine is not null && ammo == magazine.Ammunition
            ? Math.Min(magazine.Loaded, _nativeInventory.Item(magazine.Ammunition)?.Count ?? 0) : 0;
        var total = ammo is { } ammunition ? _nativeInventory.Item(ammunition)?.Count ?? 0 : 0;
        var range = source.MaximumRange * _configuration.World.GameUnitsToMeters;
        string? limit = null;
        string? error = BotCombatTelemetry.Error(view, "error") ?? BotCombatTelemetry.Error(handling, "error");
        if (source.Automatic || source.AmmoUse != 1 || source.ClipSize == 0 || !source.HasAmmunitionSource ||
            source.IsMeleeWeapon || source.IsThrownWeapon || source.IsMine)
            limit = "Bot skill scope is nonautomatic, one-round magazine-fed instant rays; other original weapon owners remain independent.";
        FalloutWeaponShot? shot = null;
        if (limit is null && ammo is { } selected)
        {
            if (_botCombatWeaponIdentity != (reference, selected))
            {
                var prepared = FalloutWeaponShot.Read(_nativePluginStack!, reference, selected);
                _botCombatWeaponIdentity = (reference, selected); _botCombatWeaponSource = source;
                _botCombatShotSource = prepared;
            }
            shot = _botCombatShotSource;
            if (shot!.Projectiles != 1 || !shot.Projectile.IsInstantRayAttack || shot.Projectile.ExplosionSource is not null)
                limit = "Bot skill scope requires exactly one nonexplosive instant-ray projectile.";
            else
            {
                try { shot.RequireInstantRay(); }
                catch (NotSupportedException refusal) { limit = refusal.Message; }
                range = Math.Min(range, shot.Projectile.Range * _configuration.World.GameUnitsToMeters);
            }
        }
        else if (limit is null) limit = "Bot skill requires carried source ammunition; ordinary inventory or retreat is required.";
        if (!float.IsFinite(range) || range <= 0) error ??= "Source weapon/projectile range is not finite and positive.";
        if (limit is null && snapshot.Drawn)
        {
            var name = presentation.GetProperty("thirdPerson").GetBoolean() ? "NativeThirdPersonBody" : "NativeFirstPersonBody";
            var actor = player.FindChildren("*", "", true, false).OfType<RuntimeNativePlayerActor>()
                .SingleOrDefault(value => value.Name.ToString() == name) ??
                throw new InvalidOperationException("Actual held source weapon presentation is absent.");
            var transform = actor.ProjectileTransform();
            if (_nativeXr is null && !presentation.GetProperty("thirdPerson").GetBoolean())
                transform = player.Camera.GlobalTransform * actor.SourceCamera.AffineInverse() * transform;
            muzzle = transform.Origin; muzzleForward = -transform.Basis.Z.Normalized();
            if (!muzzle.IsFinite() || !muzzleForward.IsFinite()) error ??= "Actual source projectile socket pose is invalid.";
        }
        return new(identity, ammo?.ToString(), shot?.Projectile.Form.ToString(), snapshot.Drawn,
            presentation.GetProperty("aiming").GetBoolean(), loaded, Math.Max(0, total - loaded), range, limit is null && error is null,
            BotCombatTelemetry.Text(handling, "action"), error, limit);
    }

    private (string? Reference, string? Collider) NativeBotCombatRay(RuntimeNativePlayer player, Vector3 from, Vector3 to)
    {
        if (!from.IsFinite() || !to.IsFinite() || from.DistanceSquaredTo(to) < .0001f) return (null, "invalid-ray");
        using var query = PhysicsRayQueryParameters3D.Create(from, to, player.CollisionMask | player.CollisionLayer);
        query.CollideWithAreas = true; query.HitBackFaces = false;
        query.Exclude = new Godot.Collections.Array<Rid>(player.CombatCollisionRids);
        using var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue("collider", out var value) || value.AsGodotObject() is not Node contact)
            return (null, "no-native-source-contact");
        var reference = _nativeReferenceEvents?.CollisionReference(contact.GetInstanceId()) ??
            _nativeReferenceEvents?.AimedReference(contact)?.FormKey;
        return (reference?.ToString(), contact.GetPath().ToString());
    }

    private BotObservation NativeBotCombatControlObservation(BotCombatObservation combat)
    {
        var player = _nativePlayer!;
        return new(combat.Scene, Vector3ToNumeric(player.GlobalPosition), combat.Camera, combat.Forward,
            NumericVector.Zero, NumericVector.Zero, null, combat.Paused, player.SourceControls.Movement,
            combat.LookingEnabled, combat.CollisionResident, player.BlockingShape, "0",
            ModalInput: combat.ModalInput, Loading: combat.Loading, ExecutionFault: combat.ExecutionFault,
            Defeated: combat.Defeated, Combat: combat);
    }

    private static FalloutFormKey BotSourceReference(string identity)
    {
        var separator = identity.LastIndexOf(':');
        if (separator <= 0 || !uint.TryParse(identity.AsSpan(separator + 1), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out var objectId)) throw new InvalidDataException("Combat source identity is not plugin:hex.");
        return new(identity[..separator], objectId);
    }
}
