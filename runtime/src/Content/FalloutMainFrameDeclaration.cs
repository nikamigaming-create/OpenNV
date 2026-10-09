using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutMainFrameDeclaration(string ExecutableSha256, string Contract, uint ConstructorWord)
{
    private const string NewVegas = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string Fallout3 = "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e";
    internal const uint ForcedProcessing = 2, PermitsForcedQueue = 8;
    internal static IEnumerable<string> Executables => [NewVegas, Fallout3];
    internal static FalloutMainFrameDeclaration ForExecutable(string executable)
    {
        _ = FalloutActorProcessRuntimeDeclaration.ForExecutable(executable);
        var initial = executable switch
        {
            NewVegas => 0u,
            Fallout3 => 1u,
            _ => throw new NotSupportedException("Selected Main constructor/frame declaration is unowned.")
        };
        const string rules = "main-frame/v1;constructor-complete-word;forced-mask-two;queue-window-mask-eight;" +
            "window-set-before-ordered-consumers-clear-after-final-child;failed-prefix-keeps-word;" +
            "optional-working-context-five;first-entry-walk-before-prelude;second-walk-before-array-release;" +
            "priority-same-CELL-instance-cache-before-inputs;unsigned-grid-half;" +
            "position-source-Float32-FISTP-current-rounding-then-signed-shift-twelve;" +
            "signed-axis-max-before-unchecked-abs;unchecked-multiply-ten-add-ten-byte-wrap;" +
            "byte-at-most-twenty-case-one-otherwise-case-three;" +
            "inline-only-if-caller-permits-and-source-thread-equal-and-independent-gate-clear;" +
            "queued-task-ctor-zero-key-zero-state-byte-priority;child-order-before-parent-full-int-key-change";
        return new(executable, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable + "\0" + rules + "\0" + initial))).ToLowerInvariant(), initial);
    }
    internal void Validate()
    {
        if (this != ForExecutable(ExecutableSha256)) throw new InvalidDataException("Selected Main/frame source declaration drifted.");
    }
}
