using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuestTarget(
    FalloutFormKey Quest,
    uint Objective,
    int Ordinal,
    FalloutFormKey Reference,
    byte Flags,
    IReadOnlyList<FalloutCondition> Conditions);

internal static class FalloutQuestTargets
{
    private const int TargetBytes = 8;
    private const byte KnownTargetFlags = 0x01;
    private static readonly HashSet<string> ReferenceTypes = ["REFR", "ACHR", "ACRE", "PGRE", "PMIS"];

    internal static IReadOnlyList<FalloutQuestTarget> Read(FalloutPluginStack records, FalloutFormKey quest)
    {
        ArgumentNullException.ThrowIfNull(records);
        var owner = records.GetEffective(quest);
        if (owner.Signature != "QUST") throw Error(owner, "target owner is not QUST");
        var targets = new List<FalloutQuestTarget>();
        var objectives = new HashSet<uint>();
        uint? objective = null;
        var hasDescription = false;
        FalloutQuestTarget? target = null;
        var conditions = new List<FalloutCondition>();
        var sourceOrdinal = 0;

        void EndTarget()
        {
            if (target is null) return;
            targets.Add(target with { Conditions = conditions.AsReadOnly() });
            target = null;
            conditions = [];
        }

        void EndObjective()
        {
            EndTarget();
            if (objective is { } index && !hasDescription)
                throw Error(owner, $"QOBJ {index} has no NNAM description");
            objective = null;
            hasDescription = false;
        }

        foreach (var field in owner.ReadSubrecords())
        {
            switch (field.Signature)
            {
                case "QOBJ":
                    EndObjective();
                    if (field.Data.Length != sizeof(uint)) throw Error(owner, "QOBJ must contain one uint32 index");
                    objective = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                    if (!objectives.Add(objective.Value)) throw Error(owner, $"duplicate QOBJ {objective.Value}");
                    break;
                case "NNAM":
                    if (objective is null || hasDescription || target is not null)
                        throw Error(owner, "NNAM is outside an objective description or duplicated");
                    if (field.Data.IsEmpty || field.Data.Span.IndexOf((byte)0) != field.Data.Length - 1)
                        throw Error(owner, $"QOBJ {objective} NNAM is not one null-terminated string");
                    hasDescription = true;
                    break;
                case "QSTA":
                    EndTarget();
                    if (objective is null || !hasDescription)
                        throw Error(owner, "QSTA is outside a described QOBJ");
                    var ordinal = sourceOrdinal++;
                    if (field.Data.Length != TargetBytes)
                        throw Error(owner, $"QOBJ {objective} target {ordinal} QSTA must contain exactly {TargetBytes} bytes");
                    var flags = field.Data.Span[sizeof(uint)];
                    if ((flags & ~KnownTargetFlags) != 0)
                        throw new NotSupportedException(
                            $"{Context(owner)} QOBJ {objective} target {ordinal} has unowned QSTA flags 0x{flags:x2}.");
                    var reference = owner.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                    if (reference is null || !records.TryGetEffective(reference.Value, out var placed) ||
                        !ReferenceTypes.Contains(placed.Signature))
                        throw Error(owner, $"QOBJ {objective} target {ordinal} QSTA does not bind a winning placed reference ({reference})");
                    target = new(owner.FormKey, objective.Value, ordinal, reference.Value, flags, []);
                    break;
                case "CTDA":
                    if (objective is null) break;
                    if (target is null) throw Error(owner, $"QOBJ {objective} has CTDA outside a QSTA target");
                    try { conditions.Add(FalloutCondition.Read(owner, field.Data.Span)); }
                    catch (InvalidDataException error)
                    {
                        throw new InvalidDataException(
                            $"{Context(owner)} QOBJ {objective} target {target.Ordinal} CTDA {conditions.Count}: {error.Message}", error);
                    }
                    break;
                case "INDX":
                    EndObjective();
                    break;
                default:
                    if (objective is not null)
                        throw Error(owner, $"QOBJ {objective} contains unexpected {field.Signature} in its target scope");
                    break;
            }
        }
        EndObjective();
        return targets.AsReadOnly();
    }

    private static string Context(FalloutPluginRecord record) =>
        $"{record.Plugin.Name} {record.Signature} {record.FormKey} at 0x{record.HeaderOffset:x}";

    private static InvalidDataException Error(FalloutPluginRecord record, string detail) =>
        new($"{Context(record)} {detail}.");
}
