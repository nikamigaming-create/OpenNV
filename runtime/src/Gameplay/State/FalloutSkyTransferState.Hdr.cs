using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyTransferState
{
    private FalloutSkyHdrState? _hdr;
    private FalloutSkyHdrSnapshot? _coldHdr;
    internal FalloutSkyHdrState? Hdr => _hdr;
    internal FalloutSkyHdrState ConstructHdr(FalloutNumericIniSettings settings)
    {
        BindPresentationThread(); RequireWriter();
        if (_process == Guid.Empty) throw new InvalidOperationException("HDR factory has no actual Sky process.");
        if (_hdr is not null) return _hdr;
        // Publish before restore so a failed reconstruction remains an owned
        // failure/lifetime instead of an abandoned factory allocation.
        _hdr = new(Source, Identity, _process, settings);
        if (_coldHdr is { } cold)
        {
            try { _hdr.Restore(cold); _coldHdr = null; }
            catch (Exception failure) { _hdr.RetainFailure(failure); throw; }
        }
        Next(); return _hdr;
    }
    private FalloutSkyHdrSnapshot? CaptureHdr()
    {
        if (_coldHdr is not null) throw new NotSupportedException("source-Sky-HDR-cold-actual-native-owner-construction-pending");
        return _hdr?.Capture();
    }
    private static void ValidateHdr(FalloutSkyTransferSnapshot saved, FalloutPluginStack records)
    {
        if (saved.Hdr is not { } hdr) return; // Not constructed is retained as not constructed.
        if (hdr.CapturedSky != saved.CapturedSky || hdr.CapturedProcess != saved.CapturedProcess)
            throw new InvalidDataException("Saved HDR lost its exact shared Sky/process capture join.");
        FalloutSkyHdrState.Validate(hdr, FalloutSkyHdrSource.Read(saved.Source), hdr.Ini);
        foreach (var writer in new[] { hdr.Main, hdr.Secondary, hdr.ShaderCopy }.Where(row => row?.ImageForm is not null))
        {
            var image = FalloutImageSpaceReader.Read(records.GetEffective(writer!.ImageForm!.Value));
            if (image.FormVersion != writer.ImageFormVersion || image.DnamSha256 != writer.ImageSha256)
                throw new InvalidDataException("Saved HDR image writer changed its exact winning IMGS payload/version.");
        }
    }
    private void RestoreHdr(FalloutSkyTransferSnapshot saved)
    {
        if (_hdr is not null || _coldHdr is not null) throw new InvalidOperationException("HDR restore overwrote an existing actual field owner.");
        _coldHdr = saved.Hdr;
    }
    private void RetireHdr()
    {
        if (_hdr is null) return;
        _hdr.Retire(); _hdr = null;
    }
}
