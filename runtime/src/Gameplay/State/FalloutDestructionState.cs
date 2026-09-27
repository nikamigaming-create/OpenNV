using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutDestructionState(string SourceSha256, float Health, int Stage = -1,
    byte ModelStage = 0, bool Destroyed = false, bool Disabled = false, FalloutFormKey? Attacker = null, string? Error = null)
{
    internal static FalloutDestructionState Initial(FalloutDestructible source) => new(source.Sha256, source.Health);
    internal void Validate(FalloutDestructible source)
    {
        var reached = source.Stages.Take(Stage + 1).ToArray();
        var expectedModel = reached.LastOrDefault(stage => stage.ModelStage != 0)?.ModelStage ?? 0;
        if (!string.Equals(SourceSha256, source.Sha256, StringComparison.OrdinalIgnoreCase) || !float.IsFinite(Health) ||
            Health < 0 || Health > source.Health || Stage < -1 || Stage >= source.Stages.Count || ModelStage > 8 ||
            Stage >= 0 && Health > source.Health * source.Stages[Stage].HealthPercent / 100f ||
            ModelStage != expectedModel || Destroyed != (Health == 0 || reached.Any(stage => stage.Destroy)) ||
            Disabled != reached.Any(stage => stage.Disable))
            throw new InvalidDataException("Saved destruction state differs from its winning declaration.");
    }

    internal (FalloutDestructionState State, FalloutDestructionStage[] Entered) Damage(FalloutDestructible source,
        float damage, FalloutFormKey? attacker, bool self = false)
    {
        Validate(source);
        if (!float.IsFinite(damage) || damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        if (damage == 0 || Disabled || Error is not null || Destroyed && !self) return (this, []);
        var health = Math.Max(0, Health - damage);
        var entered = new List<FalloutDestructionStage>();
        var state = this with { Attacker = attacker ?? Attacker };
        foreach (var stage in source.Stages.Skip(Stage + 1))
        {
            var threshold = source.Health * stage.HealthPercent / 100f;
            if (health > threshold) break;
            entered.Add(stage);
            state = state with
            {
                Stage = stage.Index,
                ModelStage = stage.ModelStage == 0 ? state.ModelStage : stage.ModelStage,
                Destroyed = state.Destroyed || stage.Destroy,
                Disabled = state.Disabled || stage.Disable
            };
            if (stage.CapDamage || stage.Disable) { health = threshold; break; }
        }
        state = state with { Health = health, Destroyed = state.Destroyed || health == 0 };
        return (state, entered.ToArray());
    }
}
