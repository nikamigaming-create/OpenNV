using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutCompiledScriptEvents
{
    // This is header admission only. A native/reference producer still owns
    // the event, its subject, elapsed time and successful instruction prefix.
    internal static FalloutCompiledReference? ActorCallbackFilter(FalloutCompiledScriptProgram program,
        FalloutCompiledEvent block)
    {
        if (!program.Standalone || !program.Events.Any(item => ReferenceEquals(item, block)))
            throw new InvalidDataException("Compiled callback does not belong to the original standalone event scope.");
        if (block.Event is not (0 or 2 or 7 or 10 or 12 or 21))
            throw new NotSupportedException($"Compiled {Name(block.Event)} event filter/callback semantics are unowned.");
        if (block.Parameters.IsEmpty) return null;
        var data = block.Parameters.Span;
        if (data.Length < 2) throw new InvalidDataException("Compiled event argument count is truncated.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (count == 0 && data.Length == 2) return null;
        if (block.Event is 2 or 7 && count == 1 && data.Length == 5 && data[2] == (byte)'r')
        {
            var reference = program.Reference(BinaryPrimitives.ReadUInt16LittleEndian(data[3..]));
            // OnActivate's original header argument is deliberately not a
            // filter. SayToDone's fixed topic is an original ordered SCRO.
            if (block.Event == 7 && (reference.Form is null || reference.Variable is not null))
                throw new NotSupportedException("Compiled speech-completion variable topic filter has no live operand owner.");
            return reference;
        }
        throw new NotSupportedException("Compiled event arguments are outside the owned admission domain.");
    }
}
