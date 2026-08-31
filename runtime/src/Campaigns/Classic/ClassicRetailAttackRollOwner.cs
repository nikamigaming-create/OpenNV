namespace OpenNV.Runtime.Campaigns.Classic;

internal enum ClassicRetailAttackRollOutcome
{
    CriticalFailure,
    Failure,
    Success,
    CriticalSuccess,
}

internal sealed record ClassicRetailAttackRollResult(
    ClassicRetailRandomState RandomState,
    ClassicRetailAttackRollOutcome Outcome,
    int Margin,
    int? UpgradeRoll);

internal static class ClassicRetailAttackRollOwner
{
    internal static ClassicRetailAttackRollResult Roll(
        ClassicRetailRandomState randomState,
        ClassicRetailRandomContract randomContract,
        ClassicCriticalSelectionContract criticalContract,
        int chance,
        int criticalModifier,
        long gameTime)
    {
        randomContract.Validate();
        criticalContract.Validate();
        if (randomContract.ExactBuild != criticalContract.ExactBuild || gameTime < 0)
            throw new InvalidOperationException(
                "Classic attack roll contracts or game time are incompatible.");

        var hit = ClassicRetailRandom.Next(
            randomState,
            criticalContract.MinimumPercentRoll,
            criticalContract.MaximumPercentRoll,
            randomContract);
        var margin = chance - hit.Value;
        if (gameTime / criticalContract.TicksPerDay <
            criticalContract.CriticalUpgradeEnabledAfterDays)
        {
            return new ClassicRetailAttackRollResult(
                hit.State,
                margin >= 0
                    ? ClassicRetailAttackRollOutcome.Success
                    : ClassicRetailAttackRollOutcome.Failure,
                margin,
                null);
        }

        var upgrade = ClassicRetailRandom.Next(
            hit.State,
            criticalContract.MinimumPercentRoll,
            criticalContract.MaximumPercentRoll,
            randomContract);
        var threshold = margin >= 0
            ? criticalModifier + margin / criticalContract.CriticalUpgradeMarginDivisor
            : -margin / criticalContract.CriticalUpgradeMarginDivisor;
        var outcome = margin >= 0
            ? upgrade.Value <= threshold
                ? ClassicRetailAttackRollOutcome.CriticalSuccess
                : ClassicRetailAttackRollOutcome.Success
            : upgrade.Value <= threshold
                ? ClassicRetailAttackRollOutcome.CriticalFailure
                : ClassicRetailAttackRollOutcome.Failure;
        return new ClassicRetailAttackRollResult(
            upgrade.State,
            outcome,
            margin,
            upgrade.Value);
    }
}
