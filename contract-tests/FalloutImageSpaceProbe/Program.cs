using System.Buffers.Binary;
using System.Numerics;
using OpenNV.Runtime.Content;

var form = new FalloutFormKey("Synthetic.esm", 0x881);
Require(FalloutRendererConfiguration.Read("Device: synthetic\r\n\tShader Package  \t: 31\r\n").ShaderPackage == 31,
    "Renderer package was substituted for the configured selection.");
Expect<InvalidDataException>(() => FalloutRendererConfiguration.Read("Shader Package: 2\nShader Package: 3"));
Expect<InvalidDataException>(() => FalloutRendererConfiguration.Read("Shader Package: ../3"));
Expect<InvalidDataException>(() => FalloutRendererConfiguration.Read("Shader Package: 0"));
var classicBytes = new byte[132];
var modernBytes = new byte[148];
for (var index = 0; index < 32; index++)
{
    var value = (index + 1) / 40f;
    BinaryPrimitives.WriteSingleLittleEndian(classicBytes.AsSpan(index * 4), value);
    BinaryPrimitives.WriteSingleLittleEndian(modernBytes.AsSpan((index < 14 ? index : index + 1) * 4), value);
}
BinaryPrimitives.WriteSingleLittleEndian(modernBytes.AsSpan(56), 2.75f);
// Reserved bytes are not floats, even if they encode NaN.
classicBytes.AsSpan(128).Fill(0xff);
modernBytes.AsSpan(132).Fill(0xff);
var classic = FalloutImageSpaceReader.Decode(form, 1, classicBytes);
var modern = FalloutImageSpaceReader.Decode(form, 11, modernBytes);
Require(classic.Cinematic == modern.Cinematic && classic.Tint == modern.Tint, "Versioned cinematic channels shifted.");
Require(classic.SkinDimmer is null && modern.SkinDimmer == 2.75f, "Versioned Skin Dimmer lost its presence semantics.");
Require(classic.RawTraits.Length == 32 && modern.RawTraits.Length == 33, "Reserved DNAM bytes became traits.");
var flaggedBytes = new byte[152];
modernBytes.CopyTo(flaggedBytes, 0);
var disabled = FalloutImageSpaceReader.Decode(form, 13, flaggedBytes);
Require(disabled.Cinematic == new Vector4(1, 0, 1, 1) && disabled.Tint.W == 0, "Disabled cinematic channels remain effective.");
flaggedBytes[148] = 15;
var enabled = FalloutImageSpaceReader.Decode(form, 13, flaggedBytes);
Require(enabled.Cinematic == modern.Cinematic && enabled.Tint == modern.Tint, "Enabled cinematic channels lost source values.");
Expect<InvalidDataException>(() => FalloutImageSpaceReader.Decode(form, 11, classicBytes));
Expect<InvalidDataException>(() => FalloutImageSpaceReader.Decode(form, 1, modernBytes));
flaggedBytes[148] = 0x10;
Expect<NotSupportedException>(() => FalloutImageSpaceReader.Decode(form, 13, flaggedBytes));
BinaryPrimitives.WriteSingleLittleEndian(classicBytes, float.NaN);
Expect<InvalidDataException>(() => FalloutImageSpaceReader.Decode(form, 1, classicBytes));
Console.WriteLine("OPENNV_IMAGE_SPACE_CONTRACT_OK oldAndNewLayouts=true independentCinematicFlags=true reservedBytesPreserved=true");

var projection = FalloutCameraProjection.FromReferenceFov(90, 3);
Require(MathF.Abs(MathF.Tan(projection.VerticalFovDegrees * MathF.PI / 360) - 0.75f) < 0.000001f,
    "Horizontal reference FOV became vertical FOV.");
Expect<InvalidDataException>(() => FalloutCameraProjection.FromReferenceFov(float.NaN, 3));
Expect<InvalidDataException>(() => FalloutCameraProjection.FromReferenceFov(90, 0));
Require(FalloutRenderedMenuProjection.RenderTargetSize(1920, 1080) == (1280, 720) &&
    FalloutRenderedMenuProjection.RenderTargetSize(1024, 768) == (1280, 960) &&
    FalloutRenderedMenuProjection.RenderTargetSize(1366, 768) == (1280, 719),
    "Rendered-menu texture slots must retain their fixed width and truncated back-buffer aspect.");
Expect<ArgumentOutOfRangeException>(() => FalloutRenderedMenuProjection.RenderTargetSize(0, 720));
Expect<InvalidDataException>(() => FalloutRenderedMenuProjection.RenderTargetSize(65536, 1));
var curveBytes = Floats(0, 1, 0.5f, 3, 1, 1);
var curve = FalloutImageSpaceModifierReader.FloatCurve(curveBytes);
Require(FalloutImageSpaceModifier.Sample(curve, 0.25f, 0) == 2, "IMAD keys did not interpolate normalized time.");
Require(FalloutImageSpaceModifier.Sample(FalloutImageSpaceModifierReader.FloatCurve(Floats(0, 2, 2, 6)), 1, 0) == 4,
    "A legal knot beyond playback duration was rejected or clipped.");
Expect<InvalidDataException>(() => FalloutImageSpaceModifierReader.FloatCurve(curveBytes[..^1]));
Expect<InvalidDataException>(() => FalloutImageSpaceModifierReader.FloatCurve(Floats(0.6f, 1, 0.5f, 2)));
Expect<InvalidDataException>(() => FalloutImageSpaceModifierReader.ColorCurve(Floats(0, 1, float.NaN, 0, 1)));
var directory = Path.Combine(Path.GetTempPath(), "OpenNV-image-space-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var header = new byte[244];
    BinaryPrimitives.WriteUInt32LittleEndian(header, 1);
    BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(152), 3); // contrast multiplier key count
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(236), 2); // fade color key count
    var fade = Floats(0, 0.2f, 0.4f, 0.6f, 1, 1, 0.2f, 0.4f, 0.6f, 0);
    var path = Path.Combine(directory, "Synthetic.esm");
    void WriteFixture()
    {
        File.WriteAllBytes(path, Record("TES4", 0, Field("HEDR", Floats(1.34f, 0, 0))).Concat(
            Record("IMAD", 0x981, Field("DNAM", header).Concat(Field("EDID", "ArbitraryModifier\0"u8.ToArray()))
                .Concat(Field("18IAD", curveBytes)).Concat(Field("NAM3", fade)).ToArray())).ToArray());
    }
    WriteFixture();
    FalloutImageSpaceModifier modifier;
    using (var plugin = FalloutPlugin.Open(path)) modifier = FalloutImageSpaceModifierReader.Read(plugin.Records.Single(record => record.Signature == "IMAD"));
    var state = new FalloutImageSpaceState();
    state.Apply(modifier);
    state.Advance(1);
    var frame = state.Compose(classic);
    Require(MathF.Abs(frame.Cinematic.Z - classic.Cinematic.Z * 3) < 0.000001f && frame.Cinematic.Y == classic.Cinematic.Y,
        "IMAD contrast bound to a different cinematic channel in a legacy IMGS.");
    Require(frame.Fade == new Vector4(0.2f, 0.4f, 0.6f, 0.5f), "Fade did not use the modifier duration.");
    Require(state.Advance(1).Single().Form == modifier.Form && state.Compose(classic).Fade == Vector4.Zero,
        "Expired animated modifier retained a final color.");
    state.Apply(modifier with { Animated = false, Duration = 0.25f });
    state.Advance(25);
    Require(state.Active.Count == 1 && state.Compose(classic).Fade.W == 1, "Non-animated modifier used its ignored duration.");
    state.Remove(modifier.Form);
    Require(state.Active.Count == 0, "Remove left an active modifier.");
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(152), 2);
    WriteFixture();
    using var malformed = FalloutPlugin.Open(path);
    Expect<InvalidDataException>(() => FalloutImageSpaceModifierReader.Read(malformed.Records.Single(record => record.Signature == "IMAD")));
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("OPENNV_IMAGE_MODIFIER_CONTRACT_OK extentCounts=true normalizedTime=true lifetime=true legacyCinematic=true fade=true projectionReference=true");

if (args is [var dataRoot, var cellHex, .. var modifierIds])
{
    RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var source = RuntimeLiveContentSource.Current!;
    using var stack = FalloutPluginStack.Load(source.PluginSources);
    var selected = FalloutImageSpaceReader.ForCell(stack, stack.RuntimeFormKey(Convert.ToUInt32(cellHex, 16)))
        ?? throw new InvalidDataException("Selected owned CELL has no source image space.");
    Console.WriteLine($"OPENNV_OWNED_IMAGE_SPACE_SELECTED source={selected.Form} version={selected.FormVersion} " +
        $"cinematic={selected.Cinematic} tint={selected.Tint} sha256={selected.DnamSha256} pixelParity=unverified");
    var images = new List<FalloutImageSpace>();
    var unsupported = new List<string>();
    foreach (var record in stack.EffectiveRecords("IMGS"))
    {
        try { images.Add(FalloutImageSpaceReader.Read(record)); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            unsupported.Add($"{record.FormKey}: {error.Message}");
            Console.WriteLine($"OPENNV_OWNED_IMAGE_SPACE_DIVERGENCE {unsupported[^1]}");
        }
    }
    foreach (var group in images.GroupBy(value => (value.FormVersion, value.RawTraits.Length)))
        Console.WriteLine($"OPENNV_OWNED_IMAGE_SPACE_LAYOUT version={group.Key.FormVersion} traits={group.Key.Length} records={group.Count()}");
    Console.WriteLine($"OPENNV_OWNED_IMAGE_SPACE_AUDIT supported={images.Count} unsupported={unsupported.Count} selected={selected.Form} pixelParity=unverified");
    foreach (var id in modifierIds)
    {
        var modifier = FalloutImageSpaceModifierReader.Read(stack.GetEffective(stack.RuntimeFormKey(Convert.ToUInt32(id, 16))));
        Console.WriteLine($"OPENNV_OWNED_IMAD_SELECTED source={modifier.Form} animated={modifier.Animated} duration={modifier.Duration:R} " +
            $"scalarChannels={modifier.Multiply.Count + modifier.Add.Count + modifier.Effects.Count} tintKeys={modifier.Tint.Length} fadeKeys={modifier.Fade.Length} sha256={modifier.SourceSha256}");
    }
    var admitted = 0;
    var unknown = 0;
    foreach (var record in stack.EffectiveRecords("IMAD"))
    {
        try { _ = FalloutImageSpaceModifierReader.Read(record); admitted++; }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        { unknown++; Console.WriteLine($"OPENNV_OWNED_IMAD_DIVERGENCE source={record.FormKey} error={error.Message}"); }
    }
    Console.WriteLine($"OPENNV_OWNED_IMAD_AUDIT decoded={admitted} unsupported={unknown} visualParity=unverified");
}

static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Expect<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
static byte[] Field(string signature, byte[] data)
{
    var bytes = new byte[data.Length + 6];
    if (signature.EndsWith("IAD", StringComparison.Ordinal))
    {
        bytes[0] = byte.Parse(signature[..^3]);
        "IAD"u8.CopyTo(bytes.AsSpan(1));
    }
    else System.Text.Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length));
    data.CopyTo(bytes, 6);
    return bytes;
}
static byte[] Record(string signature, uint form, byte[] data)
{
    var bytes = new byte[data.Length + 24];
    System.Text.Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), form);
    data.CopyTo(bytes, 24);
    return bytes;
}
