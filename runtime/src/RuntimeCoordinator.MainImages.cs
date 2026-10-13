using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeImageSpace? _nativeMainImagePresenter;
    private RetailImageSpaceRenderer.Application? _nativeMainImageApplication;
    private IDisposable? _nativeMainImageAuthority;

    private void ConfigureSourceMainImageAuthority(FalloutSourceImageSpaceMainSnapshot? saved)
    {
        var world = _nativeReferences ?? throw new InvalidOperationException("Source images have no actual campaign world.");
        var main = world.CampaignMainScriptSource;
        if (main.HasNewVegasChildren)
        {
            if (saved is not null) throw new InvalidDataException("Another source family cannot restore standalone image state.");
            return;
        }
        if (_nativeImageSpaceState.HasSourceMainImages)
        {
            if (_nativeImageSpaceState.SourceMainImageProcess != world.ActualSourceProcessIdentity || saved is not null || _nativeMainImageAuthority is null)
                throw new InvalidOperationException("Source image construction repeated or changed its actual current process.");
            return;
        }
        var source = _nativePluginStack?.OwnedSource ?? throw new InvalidOperationException("Source images have no selected owned content.");
        _nativeImageSpaceState.ConfigureSourceMainImages(main, world.ActualSourceProcessIdentity,
            FalloutExecutableStringTable.ReadStandaloneImageSpaceDefaults(source.FalloutExecutablePath),
            FalloutExecutableStringTable.ReadStandaloneImageSpaceDofConstructor(source.FalloutExecutablePath, FalloutInstallationSettings.Read(source)), saved);
        _nativeImageSpaceState.ConfigureSourceImageMenu(
            FalloutExecutableStringTable.ReadStandaloneImageSpaceMenu(source.FalloutExecutablePath));
        _nativeImageSpaceState.RebindSourceImagePrograms(_nativePluginStack!);
        _nativeMainImageAuthority = world.BindCampaignSourceMainImages(_nativeImageSpaceState);
    }

    private Compositor AddSourceMainImageSpace(Node3D cellRoot, FalloutImageSpace? source)
    {
        if (_nativeMainImagePresenter is null)
        {
            var application = RetailImageSpaceRenderer.CreateFromSource(_nativeImageSpaceState.SourceMainImageDefault,
                _configuration.FalloutEnvironment.ImageSpace, _configuration.Capture,
                _configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer);
            var presenter = new RuntimeNativeImageSpace();
            // Retain each actually constructed owner before the next child.
            // The load transaction can then retire a failed factory prefix.
            _nativeMainImageApplication = application; _nativeMainImagePresenter = presenter;
            BindNativeSkyHdr(presenter);
            presenter.ConfigureSourceMain(_nativeImageSpaceState, application.Effect,
                _nativeGameTime ?? throw new NotSupportedException("Source image factory has no real calendar owner."),
                _nativeReferences!.ActualSourceProcessIdentity,
                () => _nativeReferences!.ReadCampaignSourceImageMenuClock(_nativeImageSpaceState));
            AddChild(presenter); presenter.BindSourceMainOutput();
        }
        _nativeMainImagePresenter.RequireCurrentFactory();
        _nativeImageSpaceState.PublishSourceImagePrimary(source);
        cellRoot.SetMeta("opennv_source_image_manager", _nativeMainImagePresenter.NativeIdentity.ToString());
        cellRoot.SetMeta("opennv_source_image_profile", source?.Form.ToString() ?? "source-anonymous-default");
        return (_nativeMainImageApplication ?? throw new InvalidOperationException("Current image presenter lost its real compositor.")).Compositor;
    }

    private void RetireSourceMainImageAuthority()
    {
        var failures = new List<Exception>();
        void Retire(Action action) { try { action(); } catch (Exception error) { failures.Add(error); } }
        if (_nativeMainImagePresenter is { } presenter && GodotObject.IsInstanceValid(presenter))
        {
            presenter.ProcessMode = ProcessModeEnum.Disabled;
            Retire(presenter.RetireSourceMainOutput);
            Retire(presenter.RetireSkyHdrBorrower);
            Retire(() => _nativeMainImageApplication?.Effect.ReleaseRenderingResources());
            if (failures.Count == 0) Retire(presenter.Free);
        }
        else Retire(() => _nativeMainImageApplication?.Effect.ReleaseRenderingResources());
        Retire(() => _nativeMainImageAuthority?.Dispose());
        if (failures.Count != 0) throw new AggregateException("Source image manager retains its actual failed retirement prefix.", failures);
        _nativeMainImagePresenter = null; _nativeMainImageApplication = null; _nativeMainImageAuthority = null;
    }
}
