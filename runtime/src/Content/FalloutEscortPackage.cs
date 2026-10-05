using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal enum FalloutEscortAction { ApproachTarget, Lead, WaitForTarget, Complete }

internal sealed record FalloutEscortPackage(FalloutFormKey Form, FalloutFormKey Target,
    FalloutFormKey Destination, int DestinationRadius, uint Distance, uint Flags)
{
    internal bool Running => (Flags & 0x2000) != 0;
    internal bool WeaponDrawn => (Flags & 0x800000) != 0;

    internal static FalloutEscortPackage Read(FalloutPluginRecord record)
    {
        if (record.Signature != "PACK") throw new InvalidDataException("Escort source is not PACK.");
        var fields = record.ReadSubrecords().ToArray();
        ReadOnlyMemory<byte> Required(string name, int size)
        {
            var rows = fields.Where(field => field.Signature == name).ToArray();
            return rows.Length == 1 && rows[0].Data.Length == size ? rows[0].Data :
                throw new InvalidDataException($"Escort package has invalid {name} extent/count.");
        }
        var data = FalloutPackageData.Read(record);
        if (data.Procedure != 2) throw new InvalidDataException("Package procedure is not Escort.");
        var flags = data.Flags;
        const uint supportedFlags = 0x2 | 0x1000 | 0x2000 | 0x400000 | 0x800000 | 0x1000000;
        if ((flags & ~supportedFlags) != 0 || data.BehaviorFlags != 0 || data.SpecificFlags is not (null or 0))
            throw new NotSupportedException("Escort package needs its additional behavior or search-flag owner.");
        if (fields.Any(field => field.Signature is "PLD2" or "PTD2"))
            throw new NotSupportedException("Escort search location or additional targets require their owner.");
        var location = Required("PLDT", 12).Span;
        var target = Required("PTDT", 16).Span;
        if (BinaryPrimitives.ReadInt32LittleEndian(location) != 0 || BinaryPrimitives.ReadInt32LittleEndian(target) != 0)
            throw new NotSupportedException("Escort requires its non-reference destination or target-selection owner.");
        var radius = BinaryPrimitives.ReadInt32LittleEndian(location[8..]);
        if (radius < 0) throw new InvalidDataException("Escort destination radius is negative.");
        FalloutFormKey Reference(ReadOnlySpan<byte> value) => record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(value)) ??
            throw new InvalidDataException("Escort declares a null reference.");
        return new(record.FormKey, Reference(target[4..]), Reference(location[4..]), radius,
            BinaryPrimitives.ReadUInt32LittleEndian(Required("PKE2", 4).Span), flags);
    }

    internal (FalloutEscortProgress Progress, FalloutEscortAction Action) Advance(FalloutEscortProgress progress,
        float targetDistance, float destinationDistance, float targetDestinationDistance, bool destinationReached)
    {
        progress.Validate();
        if (!float.IsFinite(targetDistance) || !float.IsFinite(destinationDistance) || !float.IsFinite(targetDestinationDistance) ||
            targetDistance < 0 || destinationDistance < 0 || targetDestinationDistance < 0)
            throw new InvalidDataException("Escort observation has invalid source distances.");
        if (progress.Complete) return (progress, FalloutEscortAction.Complete);
        if (!progress.TargetAcquired && targetDistance > Distance) return (progress, FalloutEscortAction.ApproachTarget);
        progress = progress with { TargetAcquired = true };
        if (targetDistance > Distance && targetDestinationDistance >= destinationDistance)
            return (progress, FalloutEscortAction.WaitForTarget);
        return destinationReached ? (progress with { Complete = true }, FalloutEscortAction.Complete) : (progress, FalloutEscortAction.Lead);
    }
}
