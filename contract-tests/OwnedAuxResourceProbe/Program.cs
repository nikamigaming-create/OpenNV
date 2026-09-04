using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

static byte[] Strings(params (uint Id, byte[] Value)[] rows)
{
    var data = rows.SelectMany(row => row.Value.Append((byte)0)).ToArray();
    var payload = new byte[8 + rows.Length * 8 + data.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)rows.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), (uint)data.Length);
    var offset = 0;
    for (var index = 0; index < rows.Length; ++index)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8 + index * 8), rows[index].Id);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12 + index * 8), (uint)offset);
        offset += rows[index].Value.Length + 1;
    }
    data.CopyTo(payload.AsSpan(8 + rows.Length * 8));
    return payload;
}

static byte[] LengthStrings(params (uint Id, byte[] Value)[] rows)
{
    var chunks = rows.Select(row =>
    {
        var chunk = new byte[4 + row.Value.Length + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)row.Value.Length + 1);
        row.Value.CopyTo(chunk.AsSpan(4));
        return chunk;
    }).ToArray();
    var data = chunks.SelectMany(value => value).ToArray();
    var payload = new byte[8 + rows.Length * 8 + data.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)rows.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), (uint)data.Length);
    var offset = 0;
    for (var index = 0; index < rows.Length; ++index)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8 + index * 8), rows[index].Id);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12 + index * 8), (uint)offset);
        offset += chunks[index].Length;
    }
    data.CopyTo(payload.AsSpan(8 + rows.Length * 8));
    return payload;
}

static void MustFail(Action action)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Malformed resource was accepted.");
}

var strings = BethesdaStringTable.Parse(
    Strings((0x10203040, Encoding.UTF8.GetBytes("Goodsprings"))),
    BethesdaStringTableKind.Strings);
if (strings.Count != 1 || strings[0x10203040] != "Goodsprings")
    throw new InvalidOperationException("STRINGS decode failed.");

foreach (var kind in new[] { BethesdaStringTableKind.DlStrings, BethesdaStringTableKind.IlStrings })
{
    var table = BethesdaStringTable.Parse(
        LengthStrings((7, Encoding.UTF8.GetBytes("Doc Mitchell"))), kind);
    if (table[7] != "Doc Mitchell")
        throw new InvalidOperationException($"{kind} decode failed.");
}

MustFail(() => BethesdaStringTable.Parse([1, 0, 0], BethesdaStringTableKind.Strings));
MustFail(() => BethesdaStringTable.Parse(
    Strings((1, [0xff])), BethesdaStringTableKind.Strings));
MustFail(() => BethesdaStringTable.Parse(
    Strings((1, [65]), (1, [66])), BethesdaStringTableKind.Strings));

var dds = new byte[129];
"DDS "u8.CopyTo(dds);
BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(4), 124);
BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(12), 4);
BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16), 4);
BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(76), 32);
BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(80), 4);
NativeOwnedMediaFormat.ValidateDds(dds);
MustFail(() => NativeOwnedMediaFormat.ValidateDds(dds[..128]));

var wav = new byte[46];
"RIFF"u8.CopyTo(wav);
BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), 38);
"WAVEfmt "u8.CopyTo(wav.AsSpan(8));
BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), 16);
"data"u8.CopyTo(wav.AsSpan(36));
BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), 2);
NativeOwnedMediaFormat.ValidateWav(wav);
MustFail(() => NativeOwnedMediaFormat.ValidateWav(wav[..43]));

NativeOwnedMediaFormat.ValidateMp3([0xff, 0xfb, 0x90, 0x64]);
NativeOwnedMediaFormat.ValidateMp3([.. "ID3\u0004\0\0\0\0\0\0"u8, 0xff, 0xfb, 0x90, 0x64]);
MustFail(() => NativeOwnedMediaFormat.ValidateMp3([1, 2, 3, 4]));

var ogg = new byte[35];
"OggS"u8.CopyTo(ogg);
ogg[5] = 2;
ogg[26] = 1;
ogg[27] = 7;
ogg[28] = 1;
"vorbis"u8.CopyTo(ogg.AsSpan(29));
NativeOwnedMediaFormat.ValidateOgg(ogg);
MustFail(() => NativeOwnedMediaFormat.ValidateOgg(ogg[..20]));

if (args.Length is 4 or 5)
{
    var textureArchive = new FalloutBsaArchive(args[0]);
    var soundArchive = new FalloutBsaArchive(args[2]);
    NativeOwnedMediaFormat.ValidateDds(textureArchive.Read(args[1]));
    NativeOwnedMediaFormat.ValidateWav(soundArchive.Read(args[3]));
    if (args.Length == 5)
        NativeOwnedMediaFormat.ValidateMp3(File.ReadAllBytes(args[4]));
}
else if (args.Length != 0)
{
    throw new InvalidOperationException(
        "Optional owned-corpus usage: <textures.bsa> <member.dds> <sound.bsa> <member.wav> [loose.mp3]");
}

Console.WriteLine("owned auxiliary resource probe passed");
