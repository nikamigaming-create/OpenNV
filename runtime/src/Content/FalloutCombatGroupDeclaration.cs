using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// The selected original consumers distinguish personal targets, group members
// and incoming hostile members. Native addresses are not product inputs.
internal sealed record FalloutCombatGroupDeclaration(string ExecutableSha256, string Contract)
{
    internal static FalloutCombatGroupDeclaration ReadExecutable(string path)
    {
        using var executable = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ForExecutable(Convert.ToHexString(SHA256.HashData(executable)).ToLowerInvariant());
    }

    internal static FalloutCombatGroupDeclaration Read(FalloutExperienceHudDeclaration experience)
    {
        experience.Validate();
        return ForExecutable(experience.ExecutableSha256);
    }

    internal static FalloutCombatGroupDeclaration ForExecutable(string executableSha256)
    {
        if (executableSha256 is not
            ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
             "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e"))
            throw new NotSupportedException("Selected executable has no reviewed combat-group list owner.");
        var neutral = executableSha256 + "\0combat-group-lists/v1;initial-player-group-null;" +
            "lazy-player-member-and-target;ordered-unique-members-and-targets;member-target-disjoint;" +
            "all-member-target-predicates;directed-merge-cross-check;player-split-moves-admitted-teammates;" +
            "target-removal-retains-group;empty-player-target-update-releases-membership;" +
            "player-count-is-personal-target-count;incoming-count-is-nonplayer-members;" +
            "public-player-combat-flag-is-cached-incoming-count-positive;initial-flag-clear;actual-update-order;" +
            "player-target-candidate-requires-actual-detection-positive;independent-perk-reaction-and-retirement-inputs";
        return new(executableSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(neutral))).ToLowerInvariant());
    }

    internal void Validate()
    {
        if (ForExecutable(ExecutableSha256).Contract != Contract)
            throw new InvalidDataException("Combat-group source declaration drifted.");
    }
}
