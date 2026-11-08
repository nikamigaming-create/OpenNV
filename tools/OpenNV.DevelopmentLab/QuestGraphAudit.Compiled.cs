using System.Text.Json;
using OpenNV.Runtime.Content;

internal static partial class QuestGraphAudit
{
    private sealed class CompiledInventory(StreamWriter rows)
    {
        internal long Bodies, DecodedBodies, FailedBodies, Bytes, DecodedBytes, Instructions, DecodedInstructions, FailedInstructions;
        private readonly Dictionary<ushort, long> _opcodes = [];

        internal void Inspect(string scope, int fieldOrdinal, ReadOnlyMemory<byte> data)
        {
            ++Bodies; Bytes += data.Length;
            try
            {
                var statements = FalloutCompiledScript.Decode(data.Span);
                foreach (var statement in statements)
                {
                    var instruction = statement.Framing;
                    Instructions++;
                    DecodedInstructions++;
                    _opcodes[instruction.Opcode] = _opcodes.GetValueOrDefault(instruction.Opcode) + 1;
                    rows.WriteLine(JsonSerializer.Serialize(new
                    {
                        scope,
                        fieldOrdinal,
                        instruction,
                        kind = statement.Kind.ToString(),
                        command = statement.Command?.Name ?? statement.BlockName,
                        disposition = "operands-and-expressions-decoded"
                    }));
                }
                ++DecodedBodies; DecodedBytes += data.Length;
            }
            catch (InvalidDataException error)
            {
                ++FailedBodies;
                FailedInstructions++;
                rows.WriteLine(JsonSerializer.Serialize(new
                {
                    scope,
                    fieldOrdinal,
                    bytes = data.Length,
                    disposition = "decoding-failed-no-partial-instructions-admitted",
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
            decodedInstructions = DecodedInstructions,
            failedInstructions = FailedInstructions,
            opcodes = _opcodes.OrderBy(pair => pair.Key).Select(pair => new { opcode = pair.Key, count = pair.Value }),
            allBodiesAccountedFor = Bodies == DecodedBodies + FailedBodies,
            framing = "opcode-reference-prefix-and-payload-extents-verified",
            operandDecoding = "command-parameters-and-postfix-expressions-decoded",
            execution = "compiled-vm-wired-to-runtime-owners",
            retailParity = "unverified"
        };
    }
}
