using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutCellProcessPhase : byte
{
    Constructed = 0, ReleasingData = 1, LoadingData = 2, DataLoaded = 3,
    Detaching = 4, Attaching = 5, Attached = 6,
}
internal sealed record FalloutCellProcessDeclaration(string ExecutableSha256, string Contract)
{
    internal static FalloutCellProcessDeclaration ForExecutable(string sha256)
    {
        _ = FalloutActorProcessDeclaration.ForExecutable(sha256);
        const string rules = "cell-process/v1;TESObjectCELL-constructor-zero;source-load-two-then-three;" +
            "attach-five-before-child-work-six-after;detach-entry-five-or-six-four-then-three;" +
            "source-release-one-then-zero;null-cell-false;two-three-four-secondary-only;five-six-both;" +
            "source-record-ancestry-and-current-spatial-cell-distinct;native-residency-is-not-a-phase";
        return new(sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sha256 + "\0" + rules))).ToLowerInvariant());
    }
    internal void Validate()
    {
        if (this != ForExecutable(ExecutableSha256)) throw new InvalidDataException("CELL process source declaration drifted.");
    }
    internal static bool Eligible(FalloutCellProcessPhase? current, bool high) => current is
        FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached || !high && current is
        FalloutCellProcessPhase.LoadingData or FalloutCellProcessPhase.DataLoaded or FalloutCellProcessPhase.Detaching;
}
