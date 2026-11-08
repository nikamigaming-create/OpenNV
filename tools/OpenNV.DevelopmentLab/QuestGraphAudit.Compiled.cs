using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAudit
{
    private sealed class CompiledInventory(StreamWriter rows)
    {
        internal long Bodies, DecodedBodies, FailedBodies, Bytes, DecodedBytes, Instructions;
        private readonly Dictionary<ushort, long> _opcodes = [];

        internal void Inspect(string scope, int fieldOrdinal, ReadOnlyMemory<byte> data)
        {
            ++Bodies; Bytes += data.Length;
            try
            {
                var instructions = FalloutCompiledScript.Read(data.Span);
                foreach (var instruction in instructions)
                {
                    Instructions++;
                    _opcodes[instruction.Opcode] = _opcodes.GetValueOrDefault(instruction.Opcode) + 1;
                    rows.WriteLine(JsonSerializer.Serialize(new
                    {
                        scope,
                        fieldOrdinal,
                        instruction,
                        disposition = "framed-operands-and-effects-unverified"
                    }));
                }
                ++DecodedBodies; DecodedBytes += data.Length;
            }
            catch (InvalidDataException error)
            {
                ++FailedBodies;
                rows.WriteLine(JsonSerializer.Serialize(new
                {
                    scope,
                    fieldOrdinal,
                    bytes = data.Length,
                    disposition = "framing-failed-no-partial-instructions-admitted",
                    error = error.Message
                }));
            }
        }

        internal object Summary => new
        {
            bodies = Bodies,
            decodedBodies = DecodedBodies,
            failedBodies = FailedBodies,
            bytes = Bytes,
            decodedBytes = DecodedBytes,
            instructions = Instructions,
            opcodes = _opcodes.OrderBy(pair => pair.Key).Select(pair => new { opcode = pair.Key, count = pair.Value }),
            allBodiesAccountedFor = Bodies == DecodedBodies + FailedBodies,
            framing = "opcode-reference-prefix-and-payload-extents-only",
            operandDecoding = "unimplemented",
            execution = "unimplemented",
            retailParity = "unverified"
        };
    }
}
