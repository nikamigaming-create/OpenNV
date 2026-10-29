namespace OpenNV.Runtime.Content;

// Neutral ordinary-ranged actor contract, independently recovered from the
// owned New Vegas calculation. Geometry and visibility are supplied by the
// live presentation/physics owners; melee, thrown and special targets differ.
internal sealed class FalloutVatsRangedChance(FalloutPluginStack records)
{
    internal float Calculate(float minimumSpread, float actorWobbleDegrees, float distance,
        float targetDiameter, float visibleScreenFraction, byte partToHit)
    {
        if (!float.IsFinite(minimumSpread) || minimumSpread < 0 || !float.IsFinite(actorWobbleDegrees) ||
            !float.IsFinite(distance) || distance < 0 || !float.IsFinite(targetDiameter) ||
            targetDiameter <= 0 || !float.IsFinite(visibleScreenFraction) || visibleScreenFraction is < 0 or > 1)
            throw new InvalidDataException("VATS ranged geometry or spread input is invalid.");
        float Setting(string name)
        {
            var value = FalloutGameSettingFloats.Read(records, name);
            return float.IsFinite(value) && value >= 0 ? value : throw new InvalidDataException("VATS setting is invalid: " + name);
        }
        var screenWeight = Math.Min(Setting("fVATSScreenPercentFactor"), 1);
        var screenFactor = (float)((1.0 - screenWeight) + screenWeight * (double)visibleScreenFraction);
        // Both native constants are float values widened to double here.
        const double radiansPerDegree = 0.01745329238474369;
        const double pi = 3.1415927410125732;
        var spreadAngle = (float)((minimumSpread + actorWobbleDegrees) * Setting("fVATSSpreadMult") * radiansPerDegree);
        var radius = (float)(Math.Tan(spreadAngle) * distance);
        var spreadArea = (float)(radius * (double)radius * pi);
        var targetArea = (float)(targetDiameter / 3.0 * targetDiameter);
        if (targetDiameter < radius * 2) targetArea = (float)(radius * 2 / (double)targetDiameter * targetArea);
        // Native explicitly leaves the initialized ratio zero for area <= 0.
        var ratio = spreadArea > 0 ? Math.Min(targetArea / spreadArea, Setting("fVATSRangeSpreadMax")) : 0;
        var chance = Setting("fVATSHitChanceMult") * screenFactor * ratio * partToHit;
        var maximum = Setting("fVATSMaxChance");
        return Math.Clamp(Math.Min(chance, maximum), 0, 100);
    }
}
