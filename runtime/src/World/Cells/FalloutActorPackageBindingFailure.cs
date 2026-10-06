using System.Numerics;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// This is a stopped initialization, before the selected package's begin event.
// Saving it preserves a reached failure; it does not supply the missing behavior.
internal sealed record FalloutActorPackageBindingFailure(FalloutFormKey Package, string PackageSha256,
    string Error, float[] Position, float[] Basis, bool MovingBasePose, ulong AiRandomState,
    double PollRemaining, FalloutScheduleTime? ScheduleTime, FalloutPackageRetirement Retirement,
    FalloutFaceBlinkSnapshot? Blink = null, FalloutActorPackageIdleState? IdleState = null,
    FalloutActorStoppedIdleAnimation? IndependentIdle = null)
{
    internal void Validate()
    {
        if (Package.ObjectId == 0 || string.IsNullOrWhiteSpace(Package.OwnerPlugin) ||
            PackageSha256 is not { Length: 64 } || !PackageSha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(Error) ||
            Position is not { Length: 3 } || Basis is not { Length: 9 } ||
            Position.Concat(Basis).Any(value => !float.IsFinite(value)) ||
            !double.IsFinite(PollRemaining) || PollRemaining < 0 || Retirement is null ||
            ScheduleTime is { } time && (time.Month is < 0 or > 11 || time.Date is < 1 or > 31 ||
                time.Weekday is < 0 or > 6 || !float.IsFinite(time.Hour) || time.Hour is < 0 or >= 24))
            throw new InvalidDataException("Saved actor package binding failure is invalid.");
        var x = new Vector3(Basis[0], Basis[1], Basis[2]);
        var y = new Vector3(Basis[3], Basis[4], Basis[5]);
        var z = new Vector3(Basis[6], Basis[7], Basis[8]);
        var extent = x.LengthSquared();
        if (extent <= 0 || !float.IsFinite(extent) || Math.Abs(y.LengthSquared() - extent) > extent * .001 ||
            Math.Abs(z.LengthSquared() - extent) > extent * .001 || Math.Abs(Vector3.Dot(x, y)) > extent * .001 ||
            Math.Abs(Vector3.Dot(x, z)) > extent * .001 || Math.Abs(Vector3.Dot(y, z)) > extent * .001 ||
            Vector3.Dot(Vector3.Cross(x, y), z) <= 0)
            throw new InvalidDataException("Saved actor package pose has an invalid basis.");
        Blink?.Validate();
        Retirement.Validate();
        IndependentIdle?.Validate();
        if (IndependentIdle is not null && MovingBasePose)
            throw new NotSupportedException("Stopped independent IDLE requires its stationary base pose.");
    }

    internal void Validate(FalloutPluginStack records, FalloutReferenceInstance actor)
    {
        Validate();
        if (records.GetEffective(actor.Base).Signature != "NPC_")
            throw new InvalidDataException("Saved NPC package binding failure belongs to a non-NPC reference.");
        var package = records.GetEffective(Package);
        if (package.Signature != "PACK" || !Convert.ToHexString(SHA256.HashData(package.ReadData()))
            .Equals(PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved failed package differs from its winning source.");
        var source = FalloutScriptPackage.Read(package);
        if (IdleState is { } idles)
        {
            idles.Validate(records, Package);
            if (idles.ActiveAnimation is not null || idles.Collection != new FalloutIdleCollectionPlaybackSnapshot(0, 0, 0, false))
                throw new InvalidDataException("Stopped initialization has already consumed its idle collection.");
        }
        else if (source.Idles.Count != 0)
            throw new NotSupportedException("Stopped initialization requires its declared idle collection state.");
        Retirement.Validate(records);
        IndependentIdle?.Validate(records);
        if (Blink is not null && Blink.Settings != FalloutFaceBlinkSettings.Read(records))
            throw new NotSupportedException("Saved stopped actor blink settings differ from the winning source.");
    }

    internal FalloutActorPackageBindingFailure Copy() => this with
    {
        Position = (float[])Position.Clone(),
        Basis = (float[])Basis.Clone(),
        Blink = Blink?.Copy(),
        IdleState = IdleState?.Copy(),
        IndependentIdle = IndependentIdle?.Copy()
    };
}
