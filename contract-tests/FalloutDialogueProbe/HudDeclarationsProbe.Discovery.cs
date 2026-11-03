using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static partial class HudDeclarationsProbe
{
    private static void DiscoveryDeclarations()
    {
        var code = new byte[5000];
        void Write(int at, params byte[] bytes) => bytes.CopyTo(code, at);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at), value);
        Write(100, 0x68); U32(101, 101); Write(105, 0x6a, 0xff, 0x6a, 0xff, 0x6a, 0, 0x6a, 1);
        Write(130, 0xb9); U32(131, 200); Write(135, 0xe8);
        Write(490, 0x8d, 0x94, 0x09); U32(493, 41);
        Write(500, 0x68); U32(501, 100);
        Write(544, 0x8d, 0x44, 0x12, 0xfd);
        Write(600, 0x83, 0xbd, 0, 0, 0, 0, 20, 0x7d);
        Write(2800, 0xc7, 0x45, 0xe4); U32(2803, 11);
        Write(2900, 0x8b, 0x45, 0xe4, 0x50, 0x68); U32(2905, 4025);
        Write(2930, 0xdc, 0x05); U32(2932, 300);
        Write(2950, 0xc7, 0x85); U32(2956, 14); Write(2960, 0x8d, 0x85); Write(2966, 0x50, 0xe8);
        Write(3000, 0xc7, 0x45, 0xe4); U32(3003, 12);
        Write(3100, 0x8b, 0x4d, 0xe4, 0x51, 0x68); U32(3105, 4025);
        Write(3130, 0xdc, 0x05); U32(3132, 301);
        Write(4000, 0xb9); U32(4001, 201); Write(4005, 0xe8);
        Write(4480, 0xdc, 0x1d); U32(4482, 302);
        Write(4500, 0xb9); U32(4501, 202); Write(4505, 0xe8);
        var strings = new Dictionary<uint, string> { [100] = "QuestReminder", [101] = "UISyntheticDiscovery" };
        var settings = new Dictionary<uint, string> { [200] = "sDiscoveredText", [201] = "fQuestCinematicCharacterFadeInDelay", [202] = "fQuestCinematicCharacterFadeOut" };
        FalloutDiscoveryHudDeclaration Read(byte[] input) => FalloutExecutableStringTable.ReadDiscoveryHudDeclaration(input,
            address => strings.GetValueOrDefault(address), settings, address => address switch { 300 => 13.5, 301 => 4.25, 302 => 99.25, _ => throw new InvalidDataException() });
        var value = Read(code);
        Require(value == new FalloutDiscoveryHudDeclaration("sDiscoveredText", "UISyntheticDiscovery", 11, 12, 2, -3, 41, 13.5f, 4.25f, 14, 19, 99.25f),
            "Discovery declaration replaced source fonts, resource, geometry or pool bounds with defaults.");
        void Reject(byte[] input)
        {
            try { Read(input); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Unbound discovery declaration was admitted.");
        }
        var invalid = code.ToArray(); invalid[112] = 2; Reject(invalid);
        invalid = code.ToArray(); invalid[607] = 0; Reject(invalid);
        invalid = code.ToArray(); invalid[3130] = 0; Reject(invalid);
    }
}
