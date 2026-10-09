using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// These are neutral consumers of the selected constructors, tier getter and
// common process copy. They do not assign names to unbound native pointers.
internal sealed record FalloutActorProcessRuntimeDeclaration(string ExecutableSha256, string Contract)
{
    internal static FalloutActorProcessRuntimeDeclaration ForExecutable(string executable)
    {
        _ = FalloutActorProcessDeclaration.ForExecutable(executable);
        const string rules = "actor-process-runtime/v1;Main-constructor-forced-bit-clear;" +
            "Player-constructor-signed-travel-counter-zero;positive-retains-current-tier;" +
            "Actor-constructor-neutral-life-code-zero;life-not-sleep-or-knocked-query;" +
            "forced-bit-set-before-source-full-load-or-full-update-clear-after-consumers;" +
            "common-highest-shared-class-copy-before-old-destructor-before-new-publication;" +
            "low-package-copy;low-ordered-modifier-copy;two-owned-children-move-and-clear-old;" +
            "low-stored-clock-minus-one;low-byte-zero;low-vector-positive-zero;low-three-words-zero;" +
            "low-final-three-floats-first-not-constructor-written-last-two-positive-zero;" +
            "High-source-BPTD-node-lookup-and-first-Bip01-NiBSBoneLODController-chain-retention;" +
            "nullable-node-and-controller-results-valid;selection-not-LOD-activation;" +
            "general-processing-tree-and-CELL-extra-type-nine-have-independent-producers";
        return new(executable, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executable + "\0" + rules))).ToLowerInvariant());
    }
    internal void Validate()
    {
        if (this != ForExecutable(ExecutableSha256)) throw new InvalidDataException("Selected actor process runtime declaration drifted.");
    }
    internal int InitialPlayerTravelCounter => 0;
    internal bool InitialMainForcedProcessing => false;
    internal int InitialNeutralLifeCode => 0;
    internal string HighBoneLodNode => "Bip01";
}
