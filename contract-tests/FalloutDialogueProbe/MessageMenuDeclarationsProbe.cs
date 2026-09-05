using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class MessageMenuDeclarationsProbe
{
    internal static void Run()
    {
        byte[] declaration = [0x0f, 0xb6, 0x4d, 0xf1, 0x85, 0xc9, 0x74, 7, 0xc7, 0x45, 0xe0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(declaration.AsSpan(11), 0x1234);
        string? Literal(uint address) => address == 0x1234 ? "Synthetic default" : null;
        if (FalloutExecutableStringTable.ReadShowMessageDefaultButton(declaration, Literal) != "Synthetic default")
            throw new Exception("Default message label did not retain its source text.");
        Reject(declaration, _ => null);
        Reject(declaration[..^1], Literal);
        Reject(declaration.Concat(declaration).ToArray(), Literal);
        declaration[7] = 6;
        Reject(declaration, Literal);
        declaration[7] = 7;
        declaration[5] = 0xd2;
        Reject(declaration, Literal);
    }

    private static void Reject(byte[] bytes, Func<uint, string?> literal)
    {
        try { FalloutExecutableStringTable.ReadShowMessageDefaultButton(bytes, literal); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new Exception("An incomplete or ambiguous default message declaration was accepted.");
    }
}
