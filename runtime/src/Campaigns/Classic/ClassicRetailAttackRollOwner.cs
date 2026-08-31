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
internal sealed record ClassicRetailCriticalResolution(
    ClassicRetailRandomState RandomState,
    ClassicCriticalSelection Selection,
    ClassicCriticalEffect Effect,
    int SeverityRoll,
    int? StatCheckRoll);
internal sealed record ClassicRetailDamageRoll(
    ClassicRetailRandomState RandomState,
    int Damage);

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

    internal static ClassicRetailCriticalResolution ResolveCritical(
        ClassicRetailRandomState randomState,
        ClassicRetailRandomContract randomContract,
        ClassicCriticalSelectionContract criticalContract,
        string targetKind,
        int hitLocation,
        int criticalUpgradeBonus,
        int? checkedTargetStat)
    {
        randomContract.Validate();
        criticalContract.Validate();
        if (randomContract.ExactBuild != criticalContract.ExactBuild)
            throw new InvalidOperationException(
                "Classic critical resolution contracts are incompatible.");
        var severity = ClassicRetailRandom.Next(
            randomState,
            criticalContract.MinimumPercentRoll,
            criticalContract.MaximumPercentRoll,
            randomContract);
        var selection = ClassicCriticalSelector.SelectCritical(
            criticalContract,
            hitLocation,
            severity.Value,
            criticalUpgradeBonus);
        var row = ClassicCriticalSelector.SelectCriticalEffectRow(
            criticalContract,
            targetKind,
            selection);
        if (row.Stat < 0)
        {
            if (checkedTargetStat is not null)
                throw new InvalidOperationException(
                    "Classic critical row has no source stat check.");
            return new ClassicRetailCriticalResolution(
                severity.State,
                selection,
                ClassicCriticalSelector.ResolveCriticalEffect(
                    criticalContract, targetKind, selection, null),
                severity.Value,
                null);
        }
        if (checkedTargetStat is null)
            throw new InvalidOperationException(
                "Classic critical row requires the source target stat.");
        var statCheck = ClassicCriticalSelector.RollStatCheck(
            criticalContract,
            randomContract,
            severity.State,
            row,
            checkedTargetStat.Value);
        return new ClassicRetailCriticalResolution(
            statCheck.RandomState,
            selection,
            ClassicCriticalSelector.ResolveCriticalEffect(
                criticalContract, targetKind, selection, statCheck.Succeeded),
            severity.Value,
            statCheck.Roll);
    }

    internal static ClassicRetailDamageRoll RollDamage(
        ClassicRetailRandomState randomState,
        ClassicRetailRandomContract randomContract,
        int minimumDamage,
        int maximumDamage,
        int sourceMaximumDamageBonus)
    {
        randomContract.Validate();
        if (minimumDamage < 0 || maximumDamage < minimumDamage ||
            sourceMaximumDamageBonus < 0)
            throw new InvalidOperationException(
                "Classic source damage range is invalid.");
        var result = ClassicRetailRandom.Next(
            randomState,
            minimumDamage,
            checked(maximumDamage + sourceMaximumDamageBonus),
            randomContract);
        return new ClassicRetailDamageRoll(result.State, result.Value);
    }
}
