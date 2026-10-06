using System.Globalization;
using System.Text.Json;

namespace OpenNV.Runtime.Gameplay.Bots;

// Read the native player's existing shot/action observations. Delayed impacts
// have no ordinal of their own: the skill isolates one discharge and requires a
// fresh impact token or an observed pending -> settled transition.
internal static class BotCombatTelemetry
{
    internal static BotCombatFiring ReadFiring(JsonElement handling)
    {
        var firing = handling.GetProperty("firing");
        var shots = firing.GetProperty("shots").GetInt64();
        var pending = firing.GetProperty("pendingHitscanImpacts").GetInt32();
        var error = Error(handling, "error") ?? Error(firing, "error", "damageError", "hitEventError", "muzzleError");
        if (firing.TryGetProperty("effectErrors", out var effects) && effects.ValueKind == JsonValueKind.Object)
            error ??= effects.EnumerateObject().Select(value => value.Value.GetString()).FirstOrDefault(value => value is not null);
        BotCombatShot? last = null;
        if (Object(firing, "last") is { } shot)
        {
            var weapon = shot.GetProperty("weapon").GetString()!;
            var projectile = shot.GetProperty("projectile").GetString()!;
            BotCombatImpact? immediate = null;
            if (Object(shot, "damage") is { } damage)
                immediate = new(Form(damage.GetProperty("reference")), weapon, projectile,
                    damage.GetProperty("healthDamage").GetSingle(), damage.GetProperty("died").GetBoolean(),
                    error is null, error);
            last = new(shot.GetProperty("ordinal").GetInt64(), weapon, Text(shot, "ammunition"), projectile,
                Text(shot, "reference"), Text(shot, "collider"), shot.GetProperty("distanceMeters").GetSingle(),
                shot.GetProperty("projectiles").GetInt32(), shot.GetProperty("projectileDamageEventsPending").GetInt32(), immediate);
            error ??= Error(shot, "damageError");
        }
        BotCombatImpact? impact = null;
        string? token = null;
        if (Object(firing, "lastHitscanImpact") is { } resolved)
        {
            token = resolved.GetRawText();
            var impactError = Error(resolved, "error");
            if (Text(resolved, "state") == "actor-damaged" && Text(resolved, "reference") is { } reference)
                impact = new(reference, resolved.GetProperty("weapon").GetString()!,
                    resolved.GetProperty("projectile").GetString()!, resolved.GetProperty("healthDamage").GetSingle(),
                    resolved.GetProperty("died").GetBoolean(), resolved.GetProperty("hitEventMarked").GetBoolean(), impactError);
            error ??= impactError;
        }
        if (pending == 1 && last is not null)
        {
            var requests = firing.GetProperty("pendingHitscan").EnumerateArray().ToArray();
            if (requests.Length != 1 || Text(requests[0], "weapon") != last.Weapon ||
                Text(requests[0], "projectile") != last.Projectile || Text(requests[0], "reference") != last.Reference)
                error ??= "Native pending impact does not belong to the observed single source shot.";
        }
        return new(shots, pending, last, impact, token, error);
    }

    internal static JsonElement? Object(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    internal static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static string? Error(JsonElement root, params string[] fields) =>
        fields.Select(field => Text(root, field)).FirstOrDefault(value => !string.IsNullOrEmpty(value));

    private static string Form(JsonElement reference) => reference.ValueKind == JsonValueKind.String
        ? reference.GetString()!
        : reference.GetProperty("plugin").GetString() + ":" +
            reference.GetProperty("objectId").GetUInt32().ToString("x6", CultureInfo.InvariantCulture);
}
