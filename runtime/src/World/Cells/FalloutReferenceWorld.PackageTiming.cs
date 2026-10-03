using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool PackageEligible(FalloutFormKey actor, FalloutPluginRecord package, FalloutGameTime? clock,
        FalloutFormKey? current, bool done)
    {
        if (!OncePerDay(package)) return true;
        if (current == package.FormKey && !done) return true;
        var previous = Get(actor).PackageStarts.SingleOrDefault(value => value.Package == package.FormKey);
        if (previous is null) return true;
        return (clock ?? throw new NotSupportedException("Once-per-day package has no simulation clock.")).DaysPassed - previous.DaysPassed >= 1;
    }

    internal void MarkPackageStart(FalloutFormKey actor, FalloutPluginRecord package, FalloutGameTime? clock)
    {
        if (!OncePerDay(package)) return;
        _ = Actor(actor);
        var state = Get(actor);
        var start = new FalloutPackageStart(package.FormKey, Convert.ToHexString(SHA256.HashData(package.ReadData())),
            (clock ?? throw new NotSupportedException("Once-per-day package has no simulation clock.")).DaysPassed);
        start.Validate();
        state.PackageStarts.RemoveAll(value => value.Package == package.FormKey);
        state.PackageStarts.Add(start);
    }

    private static bool OncePerDay(FalloutPluginRecord package)
    {
        var fields = package.ReadSubrecords().Where(field => field.Signature == "PKDT").ToArray();
        if (package.Signature != "PACK" || fields.Length != 1 || fields[0].Data.Length != 12)
            throw new InvalidDataException("Package timing has no source PKDT declaration.");
        return (BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span) & 0x400) != 0;
    }

    private void RestorePackageTiming(FalloutReferenceInstance instance, FalloutReferenceSnapshot snapshot)
    {
        var seen = new HashSet<FalloutFormKey>();
        foreach (var start in snapshot.PackageStarts ?? [])
        {
            _ = Actor(instance.Reference);
            var package = records.GetEffective(start.Package);
            if (!seen.Add(start.Package) || !OncePerDay(package) ||
                !Convert.ToHexString(SHA256.HashData(package.ReadData())).Equals(start.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved package timing differs from its winning source.");
            instance.PackageStarts.Add(start);
        }
        if (snapshot.PackageIdle is not { } idle) return;
        _ = Actor(instance.Reference);
        var source = records.GetEffective(idle.Package);
        var animation = records.GetEffective(idle.Idle);
        if (!Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(idle.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            FalloutScriptPackage.Read(source).Events.GetValueOrDefault(idle.Kind) != idle.Idle ||
            !Convert.ToHexString(SHA256.HashData(animation.ReadData())).Equals(idle.IdleSha256, StringComparison.OrdinalIgnoreCase) ||
            !FalloutActorIdleSource.Resolve(records, animation).AnimationPath.Equals(idle.Animation, StringComparison.OrdinalIgnoreCase) ||
            !FalloutIdleAnimationData.Read(animation).AdmitsAdditionalLoops(idle.Clock.SelectedAdditionalLoops))
            throw new InvalidDataException("Saved package event idle differs from its winning source.");
        if (idle.Kind == "POEA" && (snapshot.PackageMotion?.Package != idle.Package || snapshot.PackageMotion.Travel?.Complete != true))
            throw new InvalidDataException("Saved Travel end idle has no retained arrival.");
        instance.PackageIdle = idle;
    }
}
