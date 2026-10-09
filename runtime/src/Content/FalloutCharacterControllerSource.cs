using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// The selected process pointer's actual source type is a character controller.
// Its borrowed scene root and animation/skeleton providers are separate owners.
internal sealed record FalloutCharacterControllerSource(FalloutMainPlayerPendingSource Pending, string Contract)
{
    private const string Rules = "source-process-character-controller/v1;actual-bhkCharacterController-type;" +
        "MiddleHigh-High-constructor-owned-child-null;actual-Actor-source-load-controller-construction-and-process-owned-publication;" +
        "controller-constructor-pending-height-scalar-positive-zero;" +
        "pending-PositionZ-Float32-store-after-destination-return;store-preserves-raw-bits;" +
        "getter-reads-the-same-controller-field;zero-height-delta-positive-zero-otherwise-scalar-minus-controller-position-Z;" +
        "source-controller-position-includes-actual-proxy-transform-offset-and-source-unit-conversion;" +
        "no-skeleton-as-controller-or-Actor-origin-as-proxy-position;" +
        "real-body-and-current-process-epoch-required-before-store;" +
        "current-field-retained-in-CSharp;new-process-constructor-before-cold-field-load;" +
        "no-native-ID-pointer-or-old-body-lease-promotion;shape-mode-TLS-and-uninspected-native-controller-children-independent";

    internal static FalloutCharacterControllerSource Read(FalloutMainPlayerPendingSource pending)
    {
        pending.Validate();
        return new(pending, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            pending.Contract + "\0" + Rules))).ToLowerInvariant());
    }
    internal void Validate()
    {
        if (this != Read(Pending)) throw new InvalidDataException("Character controller changed its selected source field/factory declaration.");
    }
}
