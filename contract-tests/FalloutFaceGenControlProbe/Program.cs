using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.FaceGen;

using var buffer = new MemoryStream();
using (var output = new BinaryWriter(buffer, Encoding.Latin1, true))
{
    output.Write("FRCTL001"u8); output.Write(123U); output.Write(456U);
    foreach (var count in new[] { 2, 1, 3, 0 }) output.Write(count);
    foreach (var count in new[] { 2, 1, 3, 0 })
    {
        output.Write(count == 0 ? 0 : 1);
        if (count == 0) continue;
        for (var i = 0; i < count; i++) output.Write(i == 0 ? -0.0f : (i + 1) * 0.75f);
        var label = Encoding.Latin1.GetBytes($"Arbitrary control {count}");
        output.Write(label.Length); output.Write(label);
    }
    var value = 0;
    void Vector(int count) { for (var i = 0; i < count; i++) output.Write(++value / 1024f); }
    for (var population = 0; population < 5; population++)
        for (var attribute = 0; attribute < 2; attribute++)
            foreach (var domain in new[] { 2, 3 }) { Vector(domain); Vector(1); }
    for (var from = 0; from < 5; from++)
        for (var to = 0; to < 5; to++) if (from != to) { Vector(2); Vector(3); Vector(1); }
    for (var population = 0; population < 5; population++) { Vector(2); Vector(3); Vector(25); Vector(4); Vector(9); }
}
var bytes = buffer.ToArray();
var model = FalloutCtlFile.Read(bytes);
FaceControlContracts.Run();
Require(model.GeometryBasisVersion == 123 && model.TextureBasisVersion == 456 && model.Controls[1].Count == 1 && model.Controls[3].Count == 0,
    "CTL header, asymmetric controls or an empty source domain changed.");
Require(BitConverter.SingleToInt32Bits(model.Controls[0][0].Axis[0]) == unchecked((int)0x80000000) && model.Controls[0][0].Axis[1] == 1.5f,
    "CTL source axes were normalized or their Float32 bits changed.");
Require(model.Separations.Count == 20 && model.Separations.All(row => row.From != row.To) && model.Distributions[4].JointMatrix.Length == 25 &&
    model.Distributions[4].TextureMatrix[^1] > model.Distributions[0].TextureMatrix[^1], "CTL model domains or serialized ordering changed.");
Reject(bytes[..^1]); Reject(bytes.Concat(new byte[] { 0 }).ToArray());
var invalid = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(32), uint.MaxValue); Reject(invalid);
invalid = bytes.ToArray(); BinaryPrimitives.WriteSingleLittleEndian(invalid.AsSpan(36), float.NaN); Reject(invalid);
invalid = bytes.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(44), uint.MaxValue); Reject(invalid);
Console.WriteLine("OPENNV_FACEGEN_CONTROL_CONTRACT_PASS sourceOrder=true fullExtent=true arbitraryDimensions=true unchangedAxes=true truncationFails=true");
if (args is [var root])
{
    var declarations = FalloutExecutableStringTable.ReadFaceControls(Path.Combine(Path.GetDirectoryName(root)!, "FalloutNV.exe"));
    using var archive = new FalloutBsaArchive(Path.Combine(root, "Fallout - Misc.bsa"));
    var source = archive.Read("facegen/si.ctl");
    var owned = FalloutCtlFile.Read(source);
    foreach (var control in declarations.Controls)
    {
        Require(control.Index < owned.Controls[control.Group].Count, "Menu control indexes outside the owned CTL.");
        Console.WriteLine($"OWNED_MENU_CONTROL group={control.Group} index={control.Index} page={control.Page} label={control.Setting} " +
            $"minimum={control.Minimum.Setting ?? control.Minimum.Constant.ToString("R")} maximum={control.Maximum.Setting ?? control.Maximum.Constant.ToString("R")}");
    }
    Console.WriteLine($"OPENNV_OWNED_CONTROL_DECLARATIONS_PASS controls={declarations.Controls.Count} toneOrder={string.Join(',', declarations.TextureOrder)} source=owned-executable parity=unverified");
    Console.WriteLine($"OPENNV_OWNED_FACEGEN_CONTROL_PASS bytes={source.Length} geometryBasis={owned.GeometryBasisVersion} textureBasis={owned.TextureBasisVersion} " +
        $"dimensions={string.Join(',', owned.BasisCounts)} controls={string.Join(',', owned.Controls.Select(group => group.Count))} " +
        $"modelSlots={owned.Distributions.Count} nativeEditing=unverified");
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Reject(byte[] data)
{
    try { _ = FalloutCtlFile.Read(data); } catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Malformed CTL was accepted.");
}
