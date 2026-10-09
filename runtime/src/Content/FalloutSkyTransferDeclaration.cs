using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// A neutral constructor/reset declaration for the selected original Sky owner.
// It does not import native addresses or stand in for a living child factory.
internal sealed partial record FalloutSkyTransferDeclaration(string EngineSha256, string Contract)
{
    private const string SelectedEngine = "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    private const string Rules = "source-Sky-transfer-reset/v1;constructor-weather-current-previous-target-override-null;" +
        "constructor-cloud-and-precipitation-null;flags-thirty-two;mode-four;clock-Float32-ten;blend-one;transition-positive-zero;" +
        "loader-four-time-caches-positive-zero;noon-twelve;last-hour-source-Float32-twenty-three-point-nine-nine;" +
        "reset-mask-one-before-override-null-before-previous-null-before-current-null;transition-mask-eight-clear-before-positive-zero-store;" +
        "optional-Clouds-before-optional-Precipitation-before-four-image-instances;no-weather-target-null-store-in-reset;" +
        "Clouds-source-child-count;texture-pointer-null-before-optional-geometry-property-texture-null-before-Float32-zero;" +
        "Precipitation-second-node-parent-detach-before-null-before-first-node-parent-detach-before-null;" +
        "four-ImageSpaceModifierInstanceForm-constructor-zero-and-null;register-previous-primary-current-primary-previous-secondary-current-secondary;" +
        "enable-flags-one-and-byte-one-before-weight-updates;mode-zero-or-one-writes-four-positive-zeros;" +
        "mode-other-four-source-time-getters-in-order-and-six-weather-IMAD-slots;anonymous-engine-default-not-a-FormID;" +
        "current-primary-before-current-secondary-before-previous-primary-before-previous-secondary;" +
        "Float32-store-after-products;source-condition-does-not-normalize-or-clamp-blend;" +
        "anonymous-modifier-static-two-knots-zero-and-one-multiply-one-add-zero-effects-zero-tint-white-alpha-zero-fade-zero;" +
        "returned-prefix-is-distinct-from-later-weather-frame-native-draw-and-GPU-publication";
    internal uint ConstructorClockBits => BitConverter.SingleToUInt32Bits(10f);
    internal uint NoonBits => IsStandalone ? throw new NotSupportedException("Selected Sky has no noon image-weight consumer.") : BitConverter.SingleToUInt32Bits(12f);
    internal uint LastHourBits => BitConverter.SingleToUInt32Bits(23.99f);
    internal static string CurrentContractSha256 => Hash(Rules);
    internal static FalloutSkyTransferDeclaration Read(string path)
    {
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        return FromEngine(sha);
    }
    internal static FalloutSkyTransferDeclaration Read(FalloutPlayerRawTransferSource source)
    {
        source.Validate();
        return FromEngine(source.Pending.Player.Main.EngineSha256);
    }
    internal void Validate()
    {
        if (Contract != ContractForEngine(EngineSha256))
            throw new InvalidDataException("Sky reset changed its exact selected constructor/consumer declaration.");
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
