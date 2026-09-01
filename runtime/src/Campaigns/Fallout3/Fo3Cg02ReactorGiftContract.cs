using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal static class Fo3Cg02ReactorGiftContract
{
    internal static Fo3Cg02ReactorGiftRuntime Load(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-40-reactor-gift-runtime/v1")
            throw new InvalidOperationException(
                "Fallout 3 CG02 reactor-gift runtime identity differs.");
        var participants = source.GetProperty("participants").EnumerateArray()
            .Select(Fo3Cg02BirthdayParticipantContract.Load).ToArray();
        var stageResults = source.GetProperty("stageResults").EnumerateObject()
            .ToDictionary(property => int.Parse(property.Name,
                    System.Globalization.CultureInfo.InvariantCulture),
                property => (IReadOnlyList<Fo3Cg02ReactorGiftCommand>)property.Value
                    .GetProperty("commands").EnumerateArray().Select(row =>
                        new Fo3Cg02ReactorGiftCommand(
                            row.GetProperty("kind").GetString()!,
                            row.TryGetProperty("referenceFormId", out var reference)
                                ? reference.GetString()! : "",
                            row.TryGetProperty("itemFormId", out var item)
                                ? item.GetString()! : "",
                            row.TryGetProperty("targetFormId", out var target)
                                ? target.GetString()! : "",
                            row.TryGetProperty("targetTransform", out var transform)
                                ? Fo3Cg01Stage12Transition.LoadTransform(transform) : null,
                            row.TryGetProperty("count", out var count)
                                ? count.GetInt32() : 0,
                            row.TryGetProperty("value", out var value)
                                ? value.GetInt32() : 0,
                            row.TryGetProperty("objectiveIndex", out var objective)
                                ? objective.GetInt32() : 0,
                            row.TryGetProperty("arguments", out var arguments)
                                ? arguments.EnumerateArray().Select(value =>
                                    value.GetInt32()).ToArray()
                                : [],
                            row.TryGetProperty("questFormId", out var quest)
                                ? quest.GetString()! : "",
                            row.TryGetProperty("stage", out var stage)
                                ? stage.GetInt32() : 0)).ToArray());
        return new Fo3Cg02ReactorGiftRuntime(
            source.GetProperty("sourceStage").GetInt32(),
            source.GetProperty("jonasStage").GetInt32(),
            source.GetProperty("targetStage").GetInt32(),
            source.GetProperty("rangeStage").GetInt32(),
            source.GetProperty("hitStage").GetInt32(),
            source.GetProperty("combatStage").GetInt32(),
            source.GetProperty("deathStage").GetInt32(),
            source.GetProperty("completionStage").GetInt32(),
            participants,
            source.GetProperty("packages").GetProperty("jonasGreet")
                .GetProperty("formId").GetString()!,
            source.GetProperty("packages").GetProperty("dadGreet")
                .GetProperty("formId").GetString()!,
            source.GetProperty("packages").GetProperty("dadToRange")
                .GetProperty("formId").GetString()!,
            source.GetProperty("packages").GetProperty("dadWait")
                .GetProperty("formId").GetString()!,
            source.GetProperty("packages").GetProperty("jonasWait")
                .GetProperty("formId").GetString()!,
            source.GetProperty("targets").GetProperty("references").EnumerateArray()
                .Select(value => value.GetProperty("referenceFormId").GetString()!)
                .ToArray(),
            source.GetProperty("targets").GetProperty("animationGroup").GetString()!,
            source.GetProperty("targets").GetProperty("requiredHitCount").GetInt32(),
            source.GetProperty("targets").GetProperty("tutorialStage").GetInt32(),
            source.GetProperty("targets").GetProperty("requiredWeaponFormId")
                .GetString()!,
            LoadCombatant(source.GetProperty("combat")),
            Fo3Cg02PictureContract.Load(source.GetProperty("pictureRuntime")),
            stageResults,
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02Combatant LoadCombatant(JsonElement source) => new(
        source.GetProperty("referenceFormId").GetString()!,
        source.GetProperty("playerReferenceFormId").GetString()!,
        source.GetProperty("baseFormId").GetString()!,
        source.GetProperty("scriptFormId").GetString()!,
        source.GetProperty("packageFormId").GetString()!,
        source.GetProperty("packageTargetFormId").GetString()!,
        source.GetProperty("packageRadiusGameUnits").GetInt32(),
        source.GetProperty("maximumHealth").GetInt32(),
        source.GetProperty("weaponFormId").GetString()!,
        source.GetProperty("ammunitionFormId").GetString()!,
        source.GetProperty("weaponDamage").GetInt32(),
        source.GetProperty("clipSize").GetInt32(),
        source.GetProperty("deathStage").GetInt32());
}
