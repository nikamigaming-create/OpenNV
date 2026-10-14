using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRaceMenuModelOperand(int Instruction, uint Pointer, int ReceiverField, int StringSetter);
internal sealed record FalloutRaceMenuModelSelection(string Model, FalloutRaceMenuModelOperand? Operand, IReadOnlyList<int> Modes);
internal sealed record FalloutRaceMenuBufferAssociation(FalloutRaceMenuDevices Devices, uint Buffer, int Capacity,
    uint Operand, uint Writer, uint Call);

// This selects the constructor's input operand. It does not construct the menu
// receiver, call its string setter, or admit any of its other children.
internal sealed class FalloutRaceMenuModelPathSource
{
    internal string RuntimePath { get; }
    internal string RuntimeSha256 { get; }
    internal string Family => "PE32/ShowRaceMenu/selected-mode/PUSH-model-input";
    internal uint Instruction { get; }
    internal uint Operand => checked(Instruction + 1);
    internal uint Pointer { get; }
    internal int ReceiverField { get; }
    internal uint StringSetter { get; }
    internal string Model { get; }
    internal ImmutableArray<int> Modes { get; }
    internal ImmutableArray<byte> Preimage { get; }
    private FalloutRaceMenuModelPathSource(string path, string hash, uint codeBase, FalloutRaceMenuModelSelection selection)
    {
        var operand = selection.Operand ?? throw new NotSupportedException(
            "The selected model copy has no unique pointer operand; inline copies retain their independent source owner.");
        RuntimePath = path; RuntimeSha256 = hash; Instruction = checked(codeBase + (uint)operand.Instruction);
        Pointer = operand.Pointer; ReceiverField = operand.ReceiverField; StringSetter = checked(codeBase + (uint)operand.StringSetter);
        Model = selection.Model; Modes = [.. selection.Modes];
        var instruction = new byte[5]; instruction[0] = 0x68; BinaryPrimitives.WriteUInt32LittleEndian(instruction.AsSpan(1), Pointer);
        Preimage = [.. instruction];
    }
    internal static FalloutRaceMenuModelPathSource Read(string executable)
        => FalloutExecutableStringTable.ReadRaceMenuModelPathSource(executable);
    internal static FalloutRaceMenuModelPathSource FromSelection(string path, string hash, uint codeBase,
        FalloutRaceMenuModelSelection selection) => new(path, hash, codeBase, selection);
    internal void CheckSame(FalloutRaceMenuModelPathSource current)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(RuntimePath, current.RuntimePath) || RuntimeSha256 != current.RuntimeSha256 ||
            Instruction != current.Instruction || Pointer != current.Pointer || ReceiverField != current.ReceiverField ||
            StringSetter != current.StringSetter || Model != current.Model || !Modes.AsSpan().SequenceEqual(current.Modes.AsSpan()))
            throw new InvalidDataException("Model input changed its actual selected command/mode/receiver/source association.");
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutRaceMenuModelPathSource ReadRaceMenuModelPathSource(string executable)
    {
        var path = Path.GetFullPath(executable); var bytes = File.ReadAllBytes(path);
        var (code, image) = Load(bytes);
        return FalloutRaceMenuModelPathSource.FromSelection(path, Convert.ToHexString(SHA256.HashData(bytes)), image.CodeBase,
            ReadNativeRaceMenuSelection(code, ControlDescriptors(code, image), image.Literal,
                image.ScriptCommandStart("ShowRaceMenu", code, 0)));
    }
    internal static FalloutRaceMenuModelSelection ReadRaceMenuModelSelection(ReadOnlyMemory<byte> code,
        IReadOnlyDictionary<uint, string> settings, Func<uint, string?> literal, int command)
        => ReadNativeRaceMenuSelection(code, settings, literal, command);

    internal static FalloutRaceMenuBufferAssociation ReadRaceMenuBufferPatch(string executable, byte[] plugin)
    {
        var (nativeCode, nativeImage) = Load(executable); var (code, image) = Load(plugin);
        var command = image.ScriptCommandStart("TTW_ShowGeneProjector", code, 0, registration: true);
        return ReadRaceMenuBufferAssociation(code, image.CodeBase, command, image.Literal, image.IsWritableExtent,
            (address, count) => ReadAsciiBuffer(image.Read(address, count)), image.ImportName, nativeCode, nativeImage.CodeBase,
            checked(nativeImage.CodeBase + (uint)nativeImage.ScriptCommandStart("ShowRaceMenu", nativeCode, 0)), nativeImage.Literal);
    }
    internal static string ReadRaceMenuModelPathBytes(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        if (end <= 0 || bytes[..end].ContainsAnyExceptInRange((byte)32, (byte)126))
            throw new InvalidDataException("The current model buffer has no complete ASCII source path.");
        return MeshPath(System.Text.Encoding.ASCII.GetString(bytes[..end]));
    }
}
