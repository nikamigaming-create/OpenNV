using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private long _appearanceRevision = -1;
    private long _worldAppearanceRevision = -1;
    private FalloutReferenceWorld? _appearanceWorld;
    internal Action? AppearanceChanged { get; set; }
    internal string? AppearanceError { get; private set; }

    internal bool SynchronizeAppearance(FalloutReferenceWorld world, FalloutPluginStack stack, RuntimeLiveContentSource source,
        Func<FalloutNpcAppearance, FalloutNpcAppearancePart, FalloutNifFile, FalloutNifGeometry, Material?> materialOwner,
        int level = 1, FalloutGlobalState? globals = null)
    {
        if (AppearanceError is not null) throw new InvalidOperationException(AppearanceError);
        if (ReferenceEquals(_appearanceWorld, world) && _worldAppearanceRevision == world.AppearanceRevision) return false;
        if (!ReferenceEquals(_appearanceWorld, world)) _appearanceRevision = -1;
        var reference = Appearance.Reference ?? throw new InvalidOperationException("Actor appearance has no placed reference.");
        var revision = world.ActorAppearanceRevision(reference);
        if (_appearanceRevision == revision)
        {
            _worldAppearanceRevision = world.AppearanceRevision;
            return false;
        }
        var state = world.ActorAppearanceOverride(reference);
        // A newly assembled body can already contain this override. Later
        // revisions still invalidate it even when age-family matching selects
        // the same final race; exact same-race input never creates a revision.
        if (state is null && !Appearance.RuntimeFace && Appearance.Race == world.ActorRace(reference) ||
            _appearanceRevision < 0 && state is not null && Appearance.RuntimeFace &&
                (state.Race is null || Appearance.Race == state.Race) &&
                (state.FaceGen is null || Appearance.FaceGen.SymmetricGeometry.AsSpan().SequenceEqual(state.FaceGen.SymmetricGeometry) &&
                    Appearance.FaceGen.AsymmetricGeometry.AsSpan().SequenceEqual(state.FaceGen.AsymmetricGeometry) &&
                    Appearance.FaceGen.SymmetricTexture.AsSpan().SequenceEqual(state.FaceGen.SymmetricTexture)))
        {
            _appearanceRevision = revision;
            _appearanceWorld = world;
            _worldAppearanceRevision = world.AppearanceRevision;
            return false;
        }
        var created = new List<RuntimeNativeNifScene>();
        try
        {
            var appearance = FalloutNpcAppearanceResolver.Resolve(stack, Appearance.Npc, reference,
                world.EquippedArmor(reference, level, globals), state, world.Get(reference).Templates);
            if (!Appearance.SkeletonPath.Equals(appearance.SkeletonPath, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Appearance refresh changed the source skeleton and needs animation/contact rebinding.");
            var prepared = FalloutNpcPreparedGeometry.Read(appearance, source);
            var channels = new RuntimeNativeNifMaterialChannels();
            foreach (var part in prepared.Parts)
            {
                var scene = BuildAppearancePart(part, prepared, Skeleton, source, materialOwner, channels);
                scene.Root.Visible = false;
                created.Add(scene);
            }
            channels = Skeleton.MaterialChannels.PrepareReplacement(Parts.Select(part => part.Root).ToHashSet(), channels);
            foreach (var (animation, time) in new[] { (_baseAnimation, _baseAnimationSeconds), (_animation, _animationSeconds) })
                if (animation is not null)
                    foreach (var link in animation.Sequence.ControlledBlocks.Where(link => link.PropertyType.Length != 0))
                        (channels.Bind(animation.Source, link) ?? throw new NotSupportedException("Body replacement has no target for an active material channel."))(time);
            var targets = FaceTargets(created);
            foreach (var name in _speechFaceWeights.Keys.Concat(_faceWeights.Where(pair => pair.Value != 0).Select(pair => pair.Key)))
                if (!targets.ContainsKey(name)) throw new NotSupportedException($"Body replacement lost active face target {name}.");
            if (_blink is not null && (!targets.ContainsKey("BlinkLeft") || !targets.ContainsKey("BlinkRight")))
                throw new NotSupportedException("Body replacement lost the active bilateral eyelid targets.");
            var previous = Parts;
            Skeleton.MaterialChannels.ReplaceWith(channels);
            Appearance = appearance;
            Skeleton.Node.Scale = Vector3.One * appearance.RaceHeight;
            Parts = created.ToArray();
            _faceTargets.Clear();
            foreach (var (name, bindings) in targets) _faceTargets.Add(name, bindings);
            _faceWeights.Clear();
            _boundSources.Clear();
            foreach (var part in previous) part.Root.Free();
            foreach (var part in Parts) part.Root.Visible = true;
            created.Clear();
            if (_blink is null) ConfigureFaceAnimation(stack);
            PublishFace();
            _appearanceRevision = revision;
            _appearanceWorld = world;
            _worldAppearanceRevision = world.AppearanceRevision;
            AppearanceChanged?.Invoke();
            return true;
        }
        catch (Exception error)
        {
            AppearanceError = error.Message;
            SetMeta("opennv_appearance_divergence", AppearanceError);
            throw;
        }
        finally { foreach (var part in created) part.Root.Free(); }
    }
}
