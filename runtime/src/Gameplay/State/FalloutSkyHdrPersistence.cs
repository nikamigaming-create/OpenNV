using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSkyHdrSnapshot(string Schema, FalloutSkyHdrSource Source, Guid CapturedOwner,
    Guid CapturedShader, Guid CapturedSky, Guid CapturedProcess, long Changed, byte EnabledByte,
    bool Selected, FalloutSkyHdrIni Ini, FalloutSkyHdrWrite Main, FalloutSkyHdrWrite Secondary,
    FalloutSkyHdrWrite? ShaderCopy, FalloutSkyHdrCapability? Selection, IReadOnlyDictionary<Guid, long> ImageOrdinals);

internal sealed partial class FalloutSkyHdrState
{
    internal const string Schema = "opennv-source-Sky-HDR/v1";
    internal FalloutSkyHdrSnapshot Capture()
    {
        Require();
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        if (_selected) _ = CurrentCapability();
        var saved = new FalloutSkyHdrSnapshot(Schema, Source, Identity, ShaderIdentity, Sky, Process, _changed,
            EnabledByte, _selected, _ini, _main, _secondary, _copy, _selection, new Dictionary<Guid, long>(_imageOrdinals));
        Validate(saved, Source, Source.ReadSetting(_settings)); return saved;
    }
    internal static void Validate(FalloutSkyHdrSnapshot saved, FalloutSkyHdrSource source, FalloutSkyHdrIni ini)
    {
        if (saved.Schema != Schema || saved.Source != source || saved.CapturedOwner == Guid.Empty || saved.CapturedShader == Guid.Empty ||
            saved.CapturedSky == Guid.Empty || saved.CapturedProcess == Guid.Empty || saved.Changed < 1 || saved.EnabledByte > 1 ||
            saved.Ini != ini || saved.Selected != (saved.Selection is not null) || saved.Main is null || saved.Secondary is null ||
            saved.ImageOrdinals is null || saved.ImageOrdinals.Any(pair => pair.Key == Guid.Empty || pair.Value < 1) ||
            !saved.Selected && (saved.EnabledByte != 0 || saved.Main.Kind != FalloutSkyHdrWriteKind.Constructor ||
                saved.Secondary.Kind != FalloutSkyHdrWriteKind.Constructor || saved.ShaderCopy is not null))
            throw new InvalidDataException("Saved HDR omitted its exact current source, settings or constructor/selection writer.");
        if (saved.Selection is { } capability && (capability.Identity == Guid.Empty || capability.Process != saved.CapturedProcess ||
            capability.EngineSha256 != source.EngineSha256 || capability.Contract != source.Contract || capability.Returned < 1 ||
            !capability.NativeResourcesCurrent || string.IsNullOrWhiteSpace(capability.Producer) ||
            saved.EnabledByte != (ini.Requested && capability.HalfFloatRgbaBlendSupported ? 1 : 0)))
            throw new InvalidDataException("Saved HDR enable byte has no actual source renderer-selection return.");
        Write(saved.Main, secondary: false); Write(saved.Secondary, secondary: true);
        if (saved.ShaderCopy is { } copy)
        {
            Write(copy, secondary: false, shader: true);
            if (copy.Kind != FalloutSkyHdrWriteKind.ShaderCopy || copy.Frame is null || copy.Capability is null)
                throw new InvalidDataException("Saved Sky shader cell has no real conditional-copy writer.");
        }
        void Write(FalloutSkyHdrWrite row, bool secondary, bool shader = false)
        {
            if (row.Ordinal < 1 || row.Ordinal > saved.Changed || !Enum.IsDefined(row.Kind) ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(row.ValueBits)))
                throw new InvalidDataException("Saved HDR changed a finite ordered Float32 source field.");
            if (row.Kind == FalloutSkyHdrWriteKind.Constructor)
            {
                if (row.Ordinal != 1 || row.ValueBits != 0x3f800000 || row.Frame is not null || row.ImageOwner is not null ||
                    row.ImageForm is not null || row.ImageSha256 is not null || row.Capability is not null ||
                    row.ImageOrdinal is not null || row.ImageFormVersion is not null || row.AnonymousImageSource is not null)
                    throw new InvalidDataException("Saved HDR constructor has a fabricated producer/value.");
                return;
            }
            if (!saved.Selected || row.Capability != saved.Selection!.Identity || row.Frame is null || row.Frame == Guid.Empty ||
                !shader && row.Kind == FalloutSkyHdrWriteKind.ShaderCopy ||
                secondary && row.Kind is not (FalloutSkyHdrWriteKind.OrdinaryImage or FalloutSkyHdrWriteKind.AlternateImage) ||
                !secondary && row.Kind is FalloutSkyHdrWriteKind.AlternateImage or FalloutSkyHdrWriteKind.RendererSelection)
                throw new InvalidDataException("Saved HDR field was written by another source branch or capability.");
            if (row.ImageOwner is { } image)
            {
                if (image == Guid.Empty || (row.ImageForm is null) != (row.ImageFormVersion is null) ||
                    (row.ImageForm is null) != (row.AnonymousImageSource is not null) ||
                    row.AnonymousImageSource is { } anonymous && (anonymous != row.ImageSha256 || !FalloutSourceMainFamily.IsFallout3(source.EngineSha256)) ||
                    row.ImageSha256 is null || row.ImageSha256.Length != 64 ||
                    !row.ImageSha256.All(Uri.IsHexDigit) || row.ImageOrdinal is not { } publication || publication < 1 ||
                    !saved.ImageOrdinals.TryGetValue(image, out var highest) || publication > highest ||
                    secondary && row.Kind == FalloutSkyHdrWriteKind.OrdinaryImage && row.ValueBits != 0x3f800000)
                    throw new InvalidDataException("Saved HDR image writer lost its actual source identity/ordinary-secondary store.");
            }
            else if (row.ImageForm is not null || row.ImageSha256 is not null || row.ImageOrdinal is not null || row.ImageFormVersion is not null || row.AnonymousImageSource is not null ||
                row.Kind is not (FalloutSkyHdrWriteKind.SkyResourceLoad or FalloutSkyHdrWriteKind.ShaderCopy) || row.ValueBits != 0x3f800000)
                throw new InvalidDataException("Saved HDR scalar has no source image or resource-loader writer.");
        }
    }
    internal void Restore(FalloutSkyHdrSnapshot saved)
    {
        Require();
        if (_changed != 1 || _selected || _copy is not null || _nativeCapability is not null ||
            saved.CapturedOwner == Identity || saved.CapturedShader == ShaderIdentity || saved.CapturedSky == Sky || saved.CapturedProcess == Process)
            throw new InvalidOperationException("HDR cold restore requires a genuine new process/Sky/shader constructor.");
        Validate(saved, Source, _ini);
        _main = saved.Main; _secondary = saved.Secondary; _copy = saved.ShaderCopy;
        _changed = saved.Changed; EnabledByte = saved.EnabledByte; _selected = saved.Selected;
        _selection = saved.Selection; Next();
        foreach (var (owner, ordinal) in saved.ImageOrdinals) _imageOrdinals.Add(owner, ordinal);
        // No RID, old native callback, format-query return or frame token is
        // promoted. The reconstructed native device must execute selection.
    }
    internal void RebindRenderer(IFalloutSkyHdrCapability current)
    {
        Require();
        if (!_selected || _nativeCapability is not null || _selection is not { } previous)
            throw new InvalidOperationException("HDR renderer handoff has no detached actual capability to replace.");
        var capability = current.Read(); RequireCapability(capability);
        if (capability.Identity == previous.Identity) throw new InvalidDataException("HDR handoff reused a previous native resource lease.");
        var oldIdentity = previous.Identity;
        SelectRenderer(current);
        // Source Float32 fields persist; only the process-local capability
        // association is replaced. Historical writer/frame identities remain.
        if (_main.Capability == oldIdentity) _main = _main with { Capability = capability.Identity };
        if (_secondary.Capability == oldIdentity) _secondary = _secondary with { Capability = capability.Identity };
        if (_copy?.Capability == oldIdentity) _copy = _copy with { Capability = capability.Identity };
        Next();
    }
}
