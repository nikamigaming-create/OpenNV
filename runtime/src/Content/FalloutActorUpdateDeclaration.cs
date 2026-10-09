using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// Neutral consumers from the actual selected Actor constructors and public
// CommandInfo handlers. This byte is independent of reference enable state.
internal sealed record FalloutActorUpdateDeclaration(string ExecutableSha256, string Contract)
{
    internal static FalloutActorUpdateDeclaration ForExecutable(string sha256)
    {
        _ = FalloutActorProcessDeclaration.ForExecutable(sha256);
        const string rules = "actor-update/v1;shared-actor-constructor-byte-one;" +
            "SetActorsAI-signed-int-nonzero;ToggleActorsAI-byte-zero-test;IsActorsAIOff-byte-zero;" +
            "actor-data-load-reset-is-independent;reference-enabled-is-independent;" +
            "global-ToggleAI-task-retirement-is-independent;native-body-does-not-elect-process";
        return new(sha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sha256 + "\0" + rules))).ToLowerInvariant());
    }
    internal void Validate()
    {
        if (this != ForExecutable(ExecutableSha256)) throw new InvalidDataException("Actor update source declaration drifted.");
    }
    internal byte Initial => 1;
    internal static byte Normalize(int signed) => signed == 0 ? (byte)0 : (byte)1;
    internal static byte Toggle(byte current) => current == 0 ? (byte)1 : (byte)0;
    internal static int Off(byte current) => current == 0 ? 1 : 0;
    internal static int Integer(double number) => double.IsFinite(number) && number == Math.Truncate(number) &&
        number is >= int.MinValue and <= int.MaxValue ? checked((int)number) :
        throw new InvalidDataException("SetActorsAI requires the actual signed Int32 argument.");
}
