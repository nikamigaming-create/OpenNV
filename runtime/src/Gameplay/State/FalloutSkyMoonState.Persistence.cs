using System.Security.Cryptography;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyMoonState
{
    internal const string Schema = "opennv-source-Moons/v1";
    internal FalloutSkyMoonSnapshot Capture()
    {
        Require();
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        foreach (var (role, child) in _native)
        {
            var fields = child.Capture(); RequireNativeFields(fields);
            _moons[role] = _moons[role] with { Native = fields };
        }
        var result = new FalloutSkyMoonSnapshot(Schema, Source, _stack, Sky, _process, _changed,
            _phase, _storedDays, _climate, _moons.Values.OrderBy(row => row.Role).ToArray(), _factoryEntered, _failure);
        Validate(result, Source, _stack, _records); return result;
    }
    internal static void Validate(FalloutSkyMoonSnapshot saved, FalloutMoonSource source, string stack,
        FalloutPluginStack records)
    {
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack ||
            saved.CapturedSky == Guid.Empty || saved.CapturedProcess == Guid.Empty || saved.Changed < 1 ||
            saved.Phase is < 0 or > 7 || saved.Moons is null || saved.Failure is not null ||
            saved.Moons.Select(row => row.Role).Distinct().Count() != saved.Moons.Count ||
            saved.Moons.Select(row => row.CapturedIdentity).Distinct().Count() != saved.Moons.Count ||
            saved.FactoryEntered != (saved.Climate is not null) ||
            !saved.FactoryEntered && (saved.Phase != 0 || saved.StoredDays != 0))
            throw new InvalidDataException("Saved Moon state changed the original selection, current owners or complete factory receipt.");
        FalloutMoonClimate? climate = null;
        if (saved.Climate is { } declared)
        {
            climate = FalloutMoonClimate.Read(records.GetEffective(declared.Form));
            if (declared != climate) throw new InvalidDataException("Saved Moon climate changed its winning raw TNAM declaration.");
        }
        if (climate is null && saved.Moons.Count != 0 || climate is not null &&
            !saved.Moons.Select(row => row.Role).ToHashSet().SetEquals(Enum.GetValues<FalloutMoonRole>().Where(climate.Enabled)))
            throw new InvalidDataException("Saved Moon fields omitted or introduced a climate-enabled source child.");
        foreach (var row in saved.Moons)
        {
            if (!Enum.IsDefined(row.Role) || row.CapturedIdentity == Guid.Empty || row.Changed < 1 || row.Changed > saved.Changed ||
                row.Settings != FalloutMoonSettings.Read(records, row.Role) || row.Pending is < 0 or > 2 ||
                row.LastPhaseAttempt is < 0 or > 7 || row.Failure is not null || row.Native is null ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(row.AngleBits)) ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(row.LastHourBits)))
                throw new InvalidDataException("Saved Moon introduced unowned settings, field values or a missing native construction return.");
            RequireNativeFields(row.Native);
            if (row.Native.ParentHidden || row.Native.PrimaryHidden || row.Native.ShadowHidden ||
                row.Native.PrimaryAlphaBits != BitConverter.SingleToUInt32Bits(1f) ||
                row.Native.ShadowAlphaBits != BitConverter.SingleToUInt32Bits(1f))
                throw new InvalidDataException("Saved Moon introduced an unowned active draw/opacity writer into the admitted constructor fields.");
            Texture(row.Native.PrimaryTexture, row.Role, shadow: false);
            Texture(row.Native.ShadowTexture, row.Role, shadow: true);
            if (row.Native.PrimaryHasTexture && row.LastPhaseAttempt is null ||
                row.Native.PrimaryHasTexture && source.Texture(row.Role, row.LastPhaseAttempt!.Value) != row.Native.PrimaryTexture!.Path ||
                row.LastPhaseAttempt == 4 && row.Native.PrimaryHasTexture)
                throw new InvalidDataException("Saved Moon phase/geometry flag does not name the actual returned texture attempt.");
        }
        void Texture(FalloutMoonTexture? texture, FalloutMoonRole role, bool shadow)
        {
            if (texture is null) return;
            if (texture.Sha256.Length != 64 || !texture.Sha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(texture.ResourceSource) ||
                shadow && texture.Path != FalloutMoonSource.ShadowTexture ||
                !shadow && !Enumerable.Range(0, 8).Any(phase => source.Texture(role, phase) == texture.Path))
                throw new InvalidDataException("Saved Moon sampler is not an original role/phase resource declaration.");
            var owned = records.OwnedSource ?? throw new NotSupportedException("Moon texture validation requires the actual selected resource owner.");
            if (!owned.TryRead(texture.Path, null, out var bytes, out var identity) || identity != texture.ResourceSource ||
                Convert.ToHexString(SHA256.HashData(bytes)) != texture.Sha256)
                throw new InvalidDataException("Saved Moon sampler changed its exact winning DDS bytes/source.");
        }
    }
    internal void Restore(FalloutSkyMoonSnapshot saved, Guid capturedSky, Guid capturedProcess)
    {
        Require();
        if (_factoryEntered || _moons.Count != 0 || _native.Count != 0 || capturedSky != saved.CapturedSky ||
            capturedProcess != saved.CapturedProcess || Sky == capturedSky || _process == capturedProcess)
            throw new InvalidOperationException("Moon continuation requires new actual Sky/process/child lifetimes and the same captured owner join.");
        Validate(saved, Source, _stack, _records);
        _phase = saved.Phase; _storedDays = saved.StoredDays; _climate = saved.Climate;
        _factoryEntered = saved.FactoryEntered; _changed = saved.Changed;
        foreach (var row in saved.Moons)
            _moons.Add(row.Role, row with { CapturedIdentity = Guid.NewGuid(), Changed = Next() });
        // No previous native handle or thread is promoted. Retained resource
        // fields must be recreated by the genuine scene factory before capture.
        Next();
    }
}
