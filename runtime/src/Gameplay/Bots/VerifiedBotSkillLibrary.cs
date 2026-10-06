using System.Text.Json;

namespace OpenNV.Runtime.Gameplay.Bots;

// A bounded evidence library, not executable generated code or gameplay state.
// A previous success never relaxes live source, pose, input or receipt gates.
internal sealed class VerifiedBotSkillLibrary
{
    internal const string Schema = "opennv-verified-bot-skills/v1";
    internal const int MaximumFeedback = 64;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<BotCombatFeedback> _feedback = [];
    internal IReadOnlyList<BotCombatFeedback> Feedback => _feedback;
    internal object State => new
    {
        schema = Schema,
        skill = ReactiveCombatSkill.SkillId,
        retainedAttempts = _feedback.Count,
        verifiedAttempts = _feedback.Count(entry => entry.Success),
        last = _feedback.LastOrDefault(),
        persistence = "private evidence only; complete gameplay saves remain independent"
    };

    internal bool IsVerified(BotSkillBinding binding, string weapon, string ammunition) =>
        _feedback.Any(entry => entry.Success && entry.Skill == ReactiveCombatSkill.SkillId &&
            entry.Binding.Build == binding.Build && entry.Binding.Source == binding.Source &&
            entry.Binding.InputMode == binding.InputMode && entry.Weapon == weapon && entry.Ammunition == ammunition);

    internal void Record(BotCombatFeedback entry)
    {
        Validate(entry);
        if (_feedback.Any(previous => previous.Attempt == entry.Attempt))
            throw new InvalidDataException("A skill attempt cannot be acknowledged twice.");
        _feedback.Add(entry);
        if (_feedback.Count > MaximumFeedback) _feedback.RemoveAt(0);
    }

    internal string Serialize()
    {
        var json = JsonSerializer.Serialize(new StoredLibrary(Schema, _feedback.ToArray()), Json);
        if (json.Length > 1_048_576) throw new InvalidDataException("The bounded skill library exceeds its storage limit.");
        return json;
    }

    internal void Restore(string json)
    {
        if (json.Length > 1_048_576) throw new InvalidDataException("The bounded skill library exceeds its storage limit.");
        var stored = JsonSerializer.Deserialize<StoredLibrary>(json, Json) ??
            throw new InvalidDataException("The skill library is absent.");
        if (stored.Schema != Schema || stored.Feedback is null || stored.Feedback.Length > MaximumFeedback)
            throw new InvalidDataException("Unknown or unbounded skill evidence library.");
        var prepared = new VerifiedBotSkillLibrary();
        foreach (var entry in stored.Feedback) prepared.Record(entry);
        _feedback.Clear();
        _feedback.AddRange(prepared._feedback);
    }

    private static void Validate(BotCombatFeedback? entry)
    {
        if (entry is null || entry.Attempt == Guid.Empty || entry.Skill != ReactiveCombatSkill.SkillId || entry.Binding is null ||
            string.IsNullOrWhiteSpace(entry.Binding.Build) || string.IsNullOrWhiteSpace(entry.Binding.Source) ||
            string.IsNullOrWhiteSpace(entry.Binding.Generation) || entry.Binding.InputMode is not ("flat" or "simulator") ||
            string.IsNullOrWhiteSpace(entry.Reference) || string.IsNullOrWhiteSpace(entry.Scene) ||
            entry.Receipts is null || entry.Receipts.Count > ReactiveCombatSkill.MaximumShots || entry.Receipts.Any(receipt => receipt is null) ||
            entry.AimRecoveries is < 0 or > ReactiveCombatSkill.MaximumShots ||
            entry.Reloads is < 0 or > ReactiveCombatSkill.MaximumShots)
            throw new InvalidDataException("Skill feedback lacks bounded source/native ownership.");
        if (!entry.Success) return;
        if (entry.Error is not null || entry.FailureKind is not null || entry.Phase != "death-observed" ||
            string.IsNullOrWhiteSpace(entry.Weapon) || string.IsNullOrWhiteSpace(entry.Ammunition) ||
            entry.Receipts.Count == 0 || !entry.Receipts[^1].VerifiedDeath)
            throw new InvalidDataException("Skill success requires an actual attributed final death receipt.");
        long? last = null;
        foreach (var receipt in entry.Receipts)
        {
            if (!receipt.VerifiedShot || receipt.Reference != entry.Reference || receipt.Weapon != entry.Weapon ||
                receipt.Ammunition != entry.Ammunition || string.IsNullOrWhiteSpace(receipt.Projectile) ||
                receipt.Aim is null || receipt.Aim.Sample != receipt.BeforeSample ||
                !receipt.Aim.Matches(entry.Reference, entry.Binding.InputMode) ||
                last is { } ordinal && receipt.BeforeShots != ordinal)
                throw new InvalidDataException("Skill success has mismatched or discontinuous shot/ammunition evidence.");
            last = receipt.Shot;
        }
    }

    private sealed record StoredLibrary(string Schema, BotCombatFeedback[] Feedback);
}
