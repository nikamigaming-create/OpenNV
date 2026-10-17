using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutPlayerPerkNumericEffects
{
    internal static float Apply(byte entryPoint, float operand, IReadOnlyList<FalloutPerkEntry> entries,
        Func<FalloutCondition, float> conditions)
    {
        if (!float.IsFinite(operand)) throw new InvalidDataException("Player perk entry operand is not finite.");
        var active = entries.Where(entry => entry.Entry == entryPoint).Where(entry =>
        {
            entry.RequireActorConditionScope();
            return FalloutCondition.AllPass(entry.Conditions, conditions, evaluateRunOn: true);
        }).ToArray();
        if (active.Length > 1)
            throw new NotSupportedException($"Player perk entry {entryPoint} requires the acquired entry ordering owner.");
        if (active.Length == 0) return operand;
        var effect = active[0];
        var result = effect.Function switch
        {
            1 => effect.Value,
            2 => operand + effect.Value,
            3 => operand * effect.Value,
            _ => throw new NotSupportedException($"Player perk entry {entryPoint} function {effect.Function} is unbound."),
        };
        return float.IsFinite(result) ? result :
            throw new InvalidDataException($"Player perk entry {entryPoint} result is not finite.");
    }
}
