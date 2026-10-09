using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeSkyLayers
{
    private SourcePrecipitationResetChild? _sourcePrecipitationChild;

    private void BindSourcePrecipitation(FalloutSkyTransferState sky, Node parent, FalloutNumericIniSettings settings)
    {
        var source = FalloutSkyNativeChildSource.Read(sky.Source);
        var setting = source.Precipitation(settings);
        sky.BindPrecipitationFactory(new(source, setting));
        if (setting.Number == 0) return;
        if (_sourcePrecipitationChild is not null)
            throw new InvalidOperationException("Another Precipitation field still owns this actual scene parent.");
        // This is a source runtime field, not a proxy renderable. Its actual
        // constructor leaves both nodes null; Load binds the genuine scene root.
        var child = new SourcePrecipitationResetChild(this, parent, sky, source);
        sky.AttachChild(child); _sourcePrecipitationChild = child;
    }

    private sealed class SourcePrecipitationResetChild(RuntimeNativeSkyLayers node, Node parent,
        FalloutSkyTransferState sky, FalloutSkyNativeChildSource source) : IFalloutSkyResetChild
    {
        private readonly int _thread = System.Environment.CurrentManagedThreadId;
        private bool _retired;
        public Guid Identity { get; } = Guid.NewGuid();
        public Guid Sky => sky.Identity;
        public string Role => "Precipitation";

        private void Require(bool retirement = false)
        {
            source.Require(sky.Source);
            if (_retired || System.Environment.CurrentManagedThreadId != _thread ||
                !GodotObject.IsInstanceValid(node) || !GodotObject.IsInstanceValid(parent) ||
                !retirement && (!node.IsInsideTree() || !parent.IsInsideTree() || !parent.IsAncestorOf(node) ||
                node.IsQueuedForDeletion() || parent.IsQueuedForDeletion()))
                throw new InvalidOperationException("Precipitation lost its actual scene parent or native-thread lifetime.");
        }
        public FalloutSkyChildSnapshot Capture()
        {
            Require();
            return new(Role, source.Contract, "source-Precipitation-field", [], new(true, true, 0, true));
        }
        public FalloutSkyChildResetReturn Reset(FalloutSkyResetContext context)
        {
            Require();
            if (context.Sky != Sky || context.SourceContract != sky.Source.Contract)
                throw new InvalidDataException("Precipitation reset came from another invoking Sky source owner.");
            // Both owning node fields are known constructor nulls. There is no
            // detach/refcount operation to schedule, infer or fabricate.
            return new(context, Identity, Role, []);
        }
        public void Retire()
        {
            if (_retired) return;
            Require(retirement: true); _retired = true;
        }
    }

    private void RetireSourcePrecipitationProjection()
    {
        if (_sourcePrecipitationChild is not { } child) return;
        try { child.Retire(); }
        catch (Exception error) { _sourceTransfer?.RetainNativeChildRetirementFailure(child, error); throw; }
        _sourceTransfer?.LoseNativeChild(child, "source-Precipitation-native-parent-retired-reconstruction-pending");
    }
}
