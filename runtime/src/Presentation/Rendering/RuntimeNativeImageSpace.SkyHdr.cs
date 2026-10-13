using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeImageSpace
{
    private readonly Guid _hdrPresenterIdentity = Guid.NewGuid();
    private FalloutSkyHdrState? _sourceHdr;
    private IRuntimeSkyHdrCapability? _hdrCapability;
    private bool _hdrBorrowed;
    private string? _hdrUnowned;
    internal void BindCurrentSkyHdr(FalloutSkyHdrState source, IRuntimeSkyHdrCapability native)
    {
        if (_sourceHdr is not null || _hdrCapability is not null)
            throw new InvalidOperationException("Image-space already has a real source HDR capability owner.");
        _sourceHdr = source;
        _hdrCapability = native;
        source.AttachImageBorrower(_hdrPresenterIdentity); _hdrBorrowed = true;
        // A synchronous provider has already returned its actual capability.
        // Publish that source store before telemetry can observe this binding.
        if (native.Returned) SelectCurrentSkyHdrCapability(source, native);
    }

    private static void SelectCurrentSkyHdrCapability(FalloutSkyHdrState source, IRuntimeSkyHdrCapability native)
    {
        if (source.RendererBoundTo(native)) return;
        try
        {
            if (source.NeedsRendererHandoff) source.RebindRenderer(native);
            else source.SelectRenderer(native);
        }
        catch (Exception failure) { source.RetainFailure(failure); throw; }
    }
    private void PublishCurrentSkyHdr(OpenNV.Runtime.Content.FalloutImageSpaceFrame gameplay)
    {
        if (_sourceHdr is not { } hdr || _hdrCapability is not { } native) return;
        try
        {
            if (!native.Returned)
            {
                _hdrUnowned = "source-Sky-HDR-native-format-query-entered-not-returned";
                SetMeta("opennv_source_sky_hdr_unowned", _hdrUnowned);
                if (UsesSourceMain) throw new NotSupportedException(_hdrUnowned);
                return;
            }
            SelectCurrentSkyHdrCapability(hdr, native);
            var input = UsesSourceMain
                ? _state.ReadSourceMainSkyHdrInput(_sourceNativeEffects ??
                    throw new InvalidOperationException("Source Main HDR has no actual same-call effect publication."), hdr, alternate: false)
                : _state.ReadCurrentSkyHdrInput(_source, gameplay, hdr, alternate: false);
            if (input.Unowned is { } unowned)
            {
                _hdrUnowned = unowned; SetMeta("opennv_source_sky_hdr_unowned", unowned);
                throw new NotSupportedException(unowned);
            }
            hdr.PublishImage(input);
            _hdrUnowned = null;
            SetMeta("opennv_source_sky_hdr_unowned", string.Empty);
            SetMeta("opennv_source_sky_hdr_owner", hdr.Identity.ToString());
            SetMeta("opennv_source_sky_hdr_enabled_byte", hdr.EnabledByte);
            SetMeta("opennv_source_sky_hdr_main_scalar_bits", hdr.MainScalarBits);
            // The alternate instance flag is a distinct original argument.
            // BeginMenuBackground alone does not supply that source caller.
        }
        catch (Exception failure) { hdr.RetainFailure(failure); throw; }
    }
    internal void RetireSkyHdrBorrower()
    {
        if (!_hdrBorrowed) return;
        _sourceHdr!.DetachImageBorrower(_hdrPresenterIdentity); _hdrBorrowed = false;
        _hdrCapability = null; _sourceHdr = null;
    }
    private void RetireIndependentImageChildren()
    {
        var failures = new List<Exception>();
        try { RetireSourceMainOutput(); } catch (Exception failure) { failures.Add(failure); }
        try { RetireSkyHdrBorrower(); } catch (Exception failure) { failures.Add(failure); }
        try { _effect?.ReleaseRenderingResources(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Image-space retains independent source-HDR/effect cleanup failures.", failures);
    }
}
