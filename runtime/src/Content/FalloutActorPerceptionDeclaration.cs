using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutDetectionScalarKind { NewVegas, Fallout3 }

// Reviewed original constructors, scalar consumers, directional cache and
// timer operations. Source addresses and instruction reports are private.
internal sealed record FalloutActorPerceptionDeclaration(string ExecutableSha256,
    FalloutDetectionScalarKind Scalar, string Contract)
{
    internal static FalloutActorPerceptionDeclaration Read(string executable)
    {
        using var input = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ForExecutable(Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant());
    }

    internal static FalloutActorPerceptionDeclaration ForExecutable(string sha256)
    {
        var scalar = sha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => FalloutDetectionScalarKind.NewVegas,
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => FalloutDetectionScalarKind.Fallout3,
            _ => throw new NotSupportedException("Selected original has no reviewed actor-perception constructor/consumer declaration."),
        };
        var neutral = sha256 + "\0actor-perception/v1;character-creature-initial-low;player-initial-high;" +
            "process-independent-native-3d;ordered-pending-detected-detecting;commit-notification-between-directions;" +
            "signed-score-positive-query;query-mode-zero-no-recompute;combat-getter-retains-int-max;" +
            "negative-observation-preserves-last-positive-position-time;float32-source-clocks;" +
            "action-countdown-tests-prior-value;light-countdown-tests-prior-value;" +
            (scalar == FalloutDetectionScalarKind.NewVegas ? "x87-stored-intermediates-level-start-armor-terms" : "sse-each-step-no-level-start-armor-terms");
        return new(sha256, scalar, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(neutral))).ToLowerInvariant());
    }

    internal void Validate()
    {
        if (this != ForExecutable(ExecutableSha256)) throw new InvalidDataException("Actor-perception declaration drifted.");
    }
}
