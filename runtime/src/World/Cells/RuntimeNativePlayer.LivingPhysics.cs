using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private PlayerLivingPhysicsOwner? _livingPhysics;

    private void PublishStandaloneLivingPhysics()
    {
        if (_standaloneSceneWorld is not { } world) return;
        if (_livingPhysics is null)
        {
            _livingPhysics = new(this, world);
            world.CampaignStandalonePlayerScene.BindMovementPhysics(_livingPhysics);
        }
        _livingPhysics.PublishModels();
    }

    private void ReleaseStandaloneLivingPhysicsModels() => _livingPhysics?.ReleaseModels();

    private void RetireStandaloneLivingPhysics()
    {
        _livingPhysics?.Dispose(); _livingPhysics = null;
    }

    // The Actor field outlives any single view/reference model. These C# models
    // own physical bodies and constraints independently of visible skeletons.
    // Native handles are adapters; the field, predicate and utility stay shared.
    private sealed class PlayerLivingPhysicsOwner : IFalloutPlayerLivingPhysicsOwner, IDisposable
    {
        private readonly RuntimeNativePlayer _player;
        private readonly FalloutReferenceWorld _world;
        private readonly int _thread = System.Environment.CurrentManagedThreadId;
        private readonly World3D _nativeWorld;
        private readonly RuntimeNativeLivingPhysicsBackend _backend;
        private readonly FalloutLivingPhysicalWorld _physicalWorld;
        private readonly FalloutPhysicalConstructorInputs _inputs;
        private readonly FalloutActorPenetrationUtility _utility;
        private readonly FalloutNativePenetrationClock _clock;
        private readonly List<FalloutLivingActorModel> _models = [];
        private readonly FalloutActorPhysicalAdmissionFields _fields = FalloutActorPhysicalAdmissionFields.Constructed;
        private FalloutActorPhysicalRegistrationTrace? _admission;
        private FalloutPlayerPhysicsFrameCall? _frame;
        private Guid? _viewFactory, _referenceFactory;
        private long _revision;
        private string? _failure;
        private bool _retired, _reportedReturn;

        internal PlayerLivingPhysicsOwner(RuntimeNativePlayer player, FalloutReferenceWorld world)
        {
            _player = player; _world = world;
            _nativeWorld = player.GetWorld3D();
            var scene = world.CampaignStandalonePlayerScene;
            Process = scene.Process; Source = scene.Source.Identity; Stack = scene.Stack;
            var records = player._physicalRecords ?? throw new InvalidOperationException("Player living field has no winning records.");
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Player living field has no owned source selection.");
            var settings = FalloutPhysicalEffectiveSettings.Read(name =>
                FalloutGameSettingFloats.ReadRetained(records, name, nameof(PlayerLivingPhysicsOwner)));
            _inputs = new(scene.Source.Main.EngineSha256, settings);
            var worldKey = "source-living-physics/" + records.RuntimeFormKey(0x14) + "/" + Stack;
            _backend = new(worldKey, ReadActualSpace, player.UnitsToMeters);
            _physicalWorld = new(_backend);
            _clock = new(FalloutPenetrationClockSource.ForExecutable(scene.Source.Main.EngineSha256));
            try
            {
                _clock.Initialize();
                var penetration = new FalloutBodyPenetrationBackend(construction =>
                    new RuntimeNativePenetrationQuery(construction, ReadSpace, player.UnitsToMeters));
                _utility = new(ReadCurrentViewModel, ReadCurrentControllerFilter,
                    () => FalloutPenetrationEffectiveSettings.Read(FalloutInstallationSettings.Read(content).Number), _clock, penetration);
            }
            catch { _clock.Dispose(); _physicalWorld.Dispose(); throw; }
        }

        public Guid Process { get; }
        public string Source { get; }
        public string Stack { get; }
        public Guid Factory { get; } = Guid.NewGuid();

        public void RequireCurrent()
        {
            RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
            if (!ReferenceEquals(_player._standaloneSceneWorld, _world) || !ReferenceEquals(_player._livingPhysics, this) ||
                _world.CampaignStandalonePlayerScene.Process != Process || _world.CampaignStandalonePlayerScene.Stack != Stack ||
                !GodotObject.IsInstanceValid(_player) || !_player.IsInsideTree() || _player.IsQueuedForDeletion() ||
                _player.GetWorld3D().Space != ReadActualSpace())
                throw new InvalidOperationException("Player living physics lost its actual source process or physical body lifetime.");
            _backend.RequireCurrent();
        }

        internal void PublishModels()
        {
            RequireCurrent();
            if (_models.Count != 0) throw new InvalidOperationException("Player living model replacement did not retire its previous objects.");
            try
            {
                var view = _player._firstPerson ?? throw new NotSupportedException("Player living view model factory is absent.");
                var reference = _player._thirdPerson ?? throw new NotSupportedException("Player living reference model factory is absent.");
                RequireActor(view); RequireActor(reference); _fields.Validate();
                var root = ReadActualPhysicalRoot();
                Construct(view, "view", root); Construct(reference, "reference", root);
                _viewFactory = view.SourceSceneFactory; _referenceFactory = reference.SourceSceneFactory;
                _admission = _world.ReadStandalonePlayerPhysicalAdmission(ReadReferenceModel, RequireModels,
                    trace => { _admission = trace; Next(); });
                // The setter uses the direct reference model; registration uses
                // the distinct current view model. Neither is a death rig.
                foreach (var body in _models[1].Bodies) body.PublishMotion(_admission.Motion!.Value);
                foreach (var model in _models)
                {
                    foreach (var body in model.Bodies) _physicalWorld.AddBody(body);
                    var admitted = new HashSet<FalloutLivingPhysicalConstraint>(ReferenceEqualityComparer.Instance);
                    foreach (var body in model.Bodies)
                        foreach (var linked in body.LinkedConstraints.OfType<FalloutLivingPhysicalConstraint>())
                            if (admitted.Add(linked)) _physicalWorld.AddConstraint(linked);
                }
                if (_admission.Register == true) _utility.Register();
                Next();
            }
            catch (Exception error)
            {
                _failure ??= error.Message; Next();
                GD.PushError("OPENNV_PLAYER_LIVING_PHYSICS_PUBLICATION " + error.Message);
            }
        }

        private void Construct(RuntimeNativePlayerActor actor, string role, FalloutCharacterControllerProxyTransform root)
        {
            var source = actor.Actor.SourceModel;
            if (actor.Weapon is not null)
                throw new NotSupportedException("source-living-WeaponObject-model-attachment-factory-unowned");
            var key = "Player/" + role + "/" + source.Skeleton.Source.Sha256;
            var poses = FalloutLivingModelConstruction.ReadSourceRestPoses(source.Skeleton, _inputs, root,
                _player._sourceScale * source.Appearance.Height, true, _ => false);
            var model = FalloutLivingActorModel.Construct(key, source, _inputs, _backend,
                scene => poses[scene.Declaration.Block.Index]);
            _models.Add(model);
        }

        public void Update(Guid main, long ordinal)
        {
            RequireCurrent();
            if (main == Guid.Empty || ordinal < 1 || _frame is { Returned: false } ||
                _frame is { } prior && prior.Main == main && prior.MainOrdinal == ordinal)
                throw new InvalidOperationException("Player living update changed its actual entered source call.");
            var entered = Next(); _frame = new(main, ordinal, entered, entered, "current-model-publication", false, null);
            try
            {
                if (_failure is not null) throw new NotSupportedException(_failure);
                RequireModels();
                if (_admission is not { Returned: true, Motion: not null, Register: not null })
                    throw new NotSupportedException("source-Player-living-model-admission-child-not-returned");
                if (_world.ReadStandalonePlayerPhysicalMode().Require() != _admission.Modes[^1].Require())
                    throw new NotSupportedException("source-living-Actor-mode-change-physical-publication-caller-unowned");
                Step("actor-penetration-utility"); _utility.Update();
                _frame = _frame with { Step = "returned", Returned = true, Changed = Next() };
                if (!_reportedReturn)
                {
                    _reportedReturn = true;
                    GD.Print($"OPENNV_PLAYER_LIVING_PHYSICS_RETURN main={main} ordinal={ordinal} " +
                        $"models={_models.Count} bodies={_models.Sum(model => model.Bodies.Count)} joints={_models.Sum(model => model.Constraints.Count)} " +
                        $"modelMotion={_admission.Motion} register={_admission.Register} " +
                        $"systems={_utility.SystemCount} wholePlayerFrame=unreturned parity=unverified");
                }
            }
            catch (Exception error)
            {
                _failure ??= error.Message; _frame = _frame with { Failure = _failure, Changed = Next() }; throw;
            }
            void Step(string step) => _frame = _frame with { Step = step, Changed = Next() };
        }

        public FalloutPlayerLivingPhysicsSnapshot Capture()
        {
            RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
            var refusals = new List<FalloutPlayerPhysicsCaptureRefusal>();
            var models = _models.Select(model => Read("model", model.Key, model.Capture)).OfType<FalloutLivingModelSnapshot>().ToArray();
            FalloutLivingWorldSnapshot? physicalWorld = null;
            if (models.Length == _models.Count) physicalWorld = Read("world", _physicalWorld.Key, _physicalWorld.Capture);
            else refusals.Add(new("world", _physicalWorld.Key, nameof(NotSupportedException),
                "Living world snapshot requires the unavailable current model captures."));
            var clock = Read("clock", Source, _clock.Capture);
            var saved = new FalloutPlayerLivingPhysicsSnapshot(FalloutPlayerLivingPhysicsSnapshot.CurrentSchema, Source, Stack,
                Process, Factory, _revision, _fields, _admission, models,
                _utility.Capture(), _frame, _failure, physicalWorld, clock, refusals.ToArray());
            saved.Validate(); return saved;

            T? Read<T>(string lane, string owner, Func<T> capture) where T : class
            {
                try { return capture(); }
                catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException)
                {
                    refusals.Add(new(lane, owner, error.GetType().Name, error.Message)); return null;
                }
            }
        }

        private IFalloutPenetrationModel ReadCurrentViewModel() { RequireModels(); return _models[0]; }
        private FalloutSourceSceneNode ReadReferenceModel()
        {
            RequireModels(); var reference = _player._thirdPerson!;
            return new(Process, reference.SourceSceneFactory, reference.GetInstanceId());
        }
        private uint? ReadCurrentControllerFilter()
        {
            RequireModels();
            // The source null arm cannot substitute for an unimplemented
            // published standard-convex controller phantom/proxy factory.
            throw new NotSupportedException("source-current-Player-standard-convex-phantom-controller-filter-producer-unowned");
        }

        private void RequireModels()
        {
            RequireCurrent();
            if (_models.Count != 2 || _player._firstPerson is not { } view || _player._thirdPerson is not { } reference ||
                view.SourceSceneFactory != _viewFactory || reference.SourceSceneFactory != _referenceFactory ||
                !ReferenceEquals(_models[0].Source, view.Actor.SourceModel) || !ReferenceEquals(_models[1].Source, reference.Actor.SourceModel))
                throw new InvalidOperationException("Player physical field cannot borrow a historical or unpublished model.");
            RequireActor(view); RequireActor(reference);
            foreach (var model in _models) model.RequireCurrent();
        }
        private static void RequireActor(RuntimeNativePlayerActor actor)
        {
            if (!GodotObject.IsInstanceValid(actor) || !actor.IsInsideTree() || actor.IsQueuedForDeletion() || actor.Error is not null)
                throw new InvalidOperationException("Player physical model has no actual completed source assembly.");
        }

        private FalloutCharacterControllerProxyTransform ReadActualPhysicalRoot()
        {
            var position = _player.GlobalPosition / _player.UnitsToMeters;
            var axes = new Basis(Vector3.Right, Vector3.Forward, Vector3.Up);
            var rotation = axes.Transposed() * _player.GlobalBasis.Orthonormalized() * axes;
            return new(new(Vector(rotation.X), Vector(rotation.Y), Vector(rotation.Z)),
                _inputs.ScaleReferencePosition(FalloutPhysicalVector3.FromValues(position.X, -position.Z, position.Y)));
            static FalloutPhysicalVector3 Vector(Vector3 value) => FalloutPhysicalVector3.FromValues(value.X, value.Y, value.Z);
        }
        private Rid ReadActualSpace()
        {
            RequireThread();
            // This retained space also owns retirement after QueueFree. Live
            // updates separately require the Player to remain in this world.
            if (!GodotObject.IsInstanceValid(_nativeWorld) || !_nativeWorld.Space.IsValid)
                throw new InvalidOperationException("Player's retained physical space is retired.");
            return _nativeWorld.Space;
        }
        private Rid ReadSpace(string world)
        {
            RequireCurrent();
            if (world != _physicalWorld.Key) throw new InvalidDataException("Penetration query requested another physical world.");
            return ReadActualSpace();
        }

        internal void ReleaseModels()
        {
            RequireRetirement();
            if (_utility.SystemCount != 0)
                throw new NotSupportedException("source-nonempty-living-model-replacement-system-retirement-caller-unowned");
            foreach (var model in _models) model.Dispose(); _models.Clear();
            _viewFactory = null; _referenceFactory = null; Next();
        }
        private void RequireThread()
        {
            if (System.Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Player physical field requires its actual calling thread.");
        }
        private long Next() => _revision = checked(_revision + 1);
        private void RequireRetirement()
        {
            RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
            _backend.RequireCurrent();
        }
        public void Dispose()
        {
            if (_retired) return; RequireRetirement(); _utility.Dispose();
            foreach (var model in _models) model.Dispose(); _models.Clear();
            _physicalWorld.Dispose(); _clock.Dispose(); _retired = true;
        }
    }
}
