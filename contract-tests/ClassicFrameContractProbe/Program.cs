using System.Buffers.Binary;
using OpenNV.Runtime.Content;

// Two directions, with two differently-sized frames each. The other four
// directions alias the first authored sequence, as real classic art can do.
var data = new byte[62 + 58];
void U32(int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);
void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), value);
void I16(int at, short value) => BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(at), value);
U32(0, 3); U16(4, 12); U16(8, 2); U32(58, 58); U32(38, 29);
I16(12, 7); I16(24, -8);
void Frame(int at, ushort width, ushort height, short x, short y, byte[] pixels)
{
    U16(at, width); U16(at + 2, height); U32(at + 4, (uint)pixels.Length);
    I16(at + 8, x); I16(at + 10, y); pixels.CopyTo(data, at + 12);
}
Frame(62, 2, 1, 1, -2, [3, 5]);
Frame(76, 1, 3, -4, 6, [8, 9, 10]);
Frame(91, 2, 1, 11, -12, [20, 21]);
Frame(105, 1, 3, -14, 16, [30, 31, 32]);
var first = Fallout1NativeFrmReader.ReadFirstFrame(data);
Require(first.Width == 2 && first.Height == 1 && first.PaletteIndexes.SequenceEqual(new byte[] { 3, 5 }), "First frame changed.");
var terminal = Fallout1NativeFrmReader.ReadFrame(data, 1, -1);
Require(terminal.Width == 1 && terminal.Height == 3 && terminal.DirectionX == 7 && terminal.DirectionY == -8 &&
    terminal.FrameX == -14 && terminal.FrameY == 16 && terminal.PaletteIndexes.SequenceEqual(new byte[] { 30, 31, 32 }),
    "Direction, stored terminal frame or source offsets changed.");
Require(Fallout1NativeFrmReader.ReadFrame(data, 5, 1).PaletteIndexes.SequenceEqual(new byte[] { 8, 9, 10 }), "Aliased direction was decoded from the wrong sequence.");
Reject(() => Fallout1NativeFrmReader.ReadFrame(data, 1, 2));
Reject(() => Fallout1NativeFrmReader.ReadFrame(data, 1, -2));
Reject(() => Fallout1NativeFrmReader.ReadFrame(data, 6, 0));
Reject(() => Fallout1NativeFrmReader.ReadFrame(data[..^1], 1, 1));
var broken = data.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(broken.AsSpan(109), 2);
Reject(() => Fallout1NativeFrmReader.ReadFrame(broken, 1, 1));
var invalidOffset = data.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(invalidOffset.AsSpan(38), uint.MaxValue);
Reject(() => Fallout1NativeFrmReader.ReadFrame(invalidOffset, 1, 0));
var invalidLength = data.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(invalidLength.AsSpan(95), uint.MaxValue);
Reject(() => Fallout1NativeFrmReader.ReadFrame(invalidLength, 1, 1));
Console.WriteLine("OPENNV_CLASSIC_FRAME_CONTRACT_PASS storedFrame=true terminalFrame=true directionOffsets=true aliases=true truncatedAndInvalid=rejected");

static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Reject(Action read)
{
    try { read(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Invalid FRM selection or payload was accepted.");
}
