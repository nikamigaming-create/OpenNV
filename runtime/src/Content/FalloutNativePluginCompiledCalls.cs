using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Content;

internal static class FalloutNativePluginCompiledCalls
{
    // The source memory must be an actual slice of this retained original SCDA.
    // A guessed cursor, diagnostic statement, new byte copy or reconstructed
    // native stream cannot authorize an original call.
    internal static FalloutNativePluginSourceCall Bind(FalloutFormKey caller, FalloutCompiledScriptProgram program,
        ushort opcode, ushort? receiver, ReadOnlyMemory<byte> payload, Func<IReadOnlyList<FalloutScriptValue>> evaluate)
    {
        var source = program.Scope.Single(field => field.Signature == "SCDA").Data;
        if (!MemoryMarshal.TryGetArray(source, out var body) || !MemoryMarshal.TryGetArray(payload, out var arguments) ||
            !ReferenceEquals(body.Array, arguments.Array) || arguments.Offset < body.Offset ||
            arguments.Count > body.Count - (arguments.Offset - body.Offset))
            throw new InvalidDataException("Native command payload is not an exact retained original SCDA slice.");
        var start = checked((uint)(arguments.Offset - body.Offset));
        return new(caller, program, opcode, receiver, start, checked(start + (uint)payload.Length), evaluate);
    }
}
