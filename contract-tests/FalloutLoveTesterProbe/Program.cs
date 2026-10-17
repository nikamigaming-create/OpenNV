using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

var strings = new Dictionary<uint, string>(); var memory = new Dictionary<uint, byte[]>(); var code = new List<byte>();
uint cursor = 100;
uint Literal(string value) { var id = cursor++; strings.Add(id, value); return id; }
uint F32(float value) { var id = cursor++; memory.Add(id, BitConverter.GetBytes(value)); return id; }
uint F64(double value) { var id = cursor++; memory.Add(id, BitConverter.GetBytes(value)); return id; }
void Operand(byte first, byte second, uint address) { code.Add(first); code.Add(second); code.AddRange(BitConverter.GetBytes(address)); }
void Push(string value) { code.Add(0x68); code.AddRange(BitConverter.GetBytes(Literal(value))); }
const string animatedPath = "meshes/test/NV_VitoMaticVigorTester_Activate.NIF";
code.AddRange(new byte[] { 0x6a, 0, 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x6a, 0 }); Push(animatedPath);
Push("meshes/test/NV_VitoMaticVigorTester_Cabinet.NIF");
foreach (var value in new[] { .2f, .3f, .4f }) Operand(0xd9, 0x05, F32(value));
Operand(0xdc, 0x1d, F64(720));
foreach (var value in new[] { -70f, -90f, -70f, -90f, -90f, -90f, -90f, -90f }) Operand(0xd9, 0x05, F32(value));
Push("First"); Operand(0xdc, 0x0d, F64(4));
for (var index = 0; index < 3; index++) Operand(0xd9, 0x05, F32(.8f));
Push("Source_Btn:0");
code.AddRange(new byte[] { 0x55, 0x8b, 0xec, 0x6a, 0xff });
Operand(0xd9, 0x05, F32(.7f)); Operand(0xdc, 0x0d, F64(Math.PI / 180)); Operand(0xdc, 0x0d, F64(.6));
Push("Surgery3DCamera");
string[] names = ["First", "Second", "ReturnFirst", "ReturnSecond"];
for (var index = 0; index < names.Length; index++) memory.Add(10004 + (uint)index * 4, BitConverter.GetBytes(Literal(names[index])));
memory.Add(10000, new byte[4]);
foreach (var address in new uint[] { 10000, 10012 }) { code.AddRange(new byte[] { 0x8b, 0x0c, 0x85 }); code.AddRange(BitConverter.GetBytes(address)); }
code.AddRange(new byte[8]);
string? ReadLiteral(uint address) => strings.GetValueOrDefault(address);
byte[] ReadMemory(uint address, int count) => memory.TryGetValue(address, out var bytes) && bytes.Length == count ? bytes : throw new InvalidDataException("Synthetic extent is absent.");
FalloutLoveTesterPresentation Read(byte[] data) => FalloutExecutableStringTable.ReadLoveTesterDeclarations(data, ReadLiteral, ReadMemory, names);
var result = Read(code.ToArray());
FalloutNifScalarKey[] stepKeys = [new(1, 3, null, null, null, 5), new(2, 7, null, null, null, 5), new(4, 11, null, null, null, 5)];
Require(new[] { 0f, 1f, 1.999f, 2f, 3.999f, 4f, 10f }.Select(time => FalloutNifAnimationSampler.SampleScalar(stepKeys, time))
    .SequenceEqual(new[] { 3f, 3f, 3f, 7f, 7f, 11f, 11f }), "Constant animation keys lost their exact step boundary.");
Require(result.AnimatedModel == animatedPath && result.WideDepth == -70 && result.NarrowDepth == -90 && result.LogicalWidthBoundary == 720, "Source transform declarations were not retained.");
Require(result.RotationRadians.SequenceEqual(new[] { .2f, .3f, .4f }) && result.LightIntensity == .8f && result.LightRadiusMultiple == 4, "Source rotations/light declarations were not retained.");
Require(result.Transition(1, 1) == "First" && result.Transition(2, 1) == "Second" && result.Transition(0, -1) == "ReturnFirst", "Ordered source page transitions were not retained.");
Require(Math.Abs(result.HorizontalSlope(60) - MathF.Tan(60 * .6f * MathF.PI / 180) * .7f) < 1e-6f, "Owned projection factors were not used.");
var damaged = code.ToArray(); var lastTable = damaged.AsSpan().LastIndexOf(new byte[] { 0x8b, 0x0c, 0x85 }); damaged[lastTable] = 0;
Reject(() => Read(damaged), "Missing reverse-page table did not fail closed.");
Reject(() => FalloutExecutableStringTable.ReadLoveTesterDeclarations(code.ToArray(), ReadLiteral, ReadMemory, names[..2]), "Missing NIF sequences did not fail closed.");
Console.WriteLine("PASS LoveTester owned declarations, projection, ordered page transitions and unsupported-table rejection.");
if (args.Length == 0) return;
if (args.Length != 1) throw new ArgumentException("Optional argument: owned FalloutNV installation directory.");
var installation = Path.GetFullPath(args[0]);
var archives = Directory.EnumerateFiles(Path.Combine(installation, "Data"), "*.bsa").Select(path => new FalloutBsaArchive(path)).ToArray();
try
{
    byte[] Owned(string path)
    {
        var loose = Path.Combine(installation, "Data", path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        if (File.Exists(loose)) return File.ReadAllBytes(loose);
        var winners = archives.Where(archive => archive.Contains(path)).ToArray();
        if (winners.Length != 1) throw new InvalidDataException($"Owned audit requires a unique selected archive member: {path}.");
        return winners[0].Read(path);
    }
    var payload = Owned("meshes/architecture/goodsprings/NV_VitoMaticVigorTester_Activate.NIF");
    var nif = FalloutNifFile.Read(payload);
    var sourceSequences = nif.Blocks.Where(block => block.TypeName == "NiControllerSequence").Select(block => (FalloutNifControllerSequence)nif.ReadObject(block.Index)).ToArray();
    var sampledChannels = 0;
    foreach (var sequence in sourceSequences)
        foreach (var link in sequence.ControlledBlocks)
        {
            var sampler = new FalloutNifAnimationSampler(nif, link.Interpolator);
            foreach (var time in new[] { sequence.StartTime, (sequence.StartTime + sequence.StopTime) / 2, sequence.StopTime })
            {
                var pose = sampler.Sample(time);
                Require(pose.Scale is null or > 0, "Owned page channel has an invalid sampled scale.");
                Require(pose.Rotation is null || float.IsFinite(pose.Rotation.Value.W), "Owned page rotation is non-finite.");
            }
            sampledChannels++;
        }
    var declaration = FalloutExecutableStringTable.ReadLoveTester(Path.Combine(installation, "FalloutNV.exe"), sourceSequences.Select(sequence => sequence.Name).ToArray());
    _ = FalloutNifFile.Read(Owned(declaration.CabinetModel));
    foreach (var path in new[] { "menus/chargen/love_tester_menu.xml", "textures/terminals/PC/BBRTOn.dds", "textures/terminals/PC/BBRTOff.dds", "textures/terminals/PC/BBLTOff.dds" }) _ = Owned(path);
    for (var number = 0; number <= 10; number++) _ = Owned($"textures/terminals/BBNumber{number}.dds");
    Console.WriteLine(JsonSerializer.Serialize(new { SourceSha256 = Convert.ToHexString(SHA256.HashData(payload)), declaration,
        Sequences = sourceSequences.Select(sequence => new { sequence.Name, sequence.StartTime, sequence.StopTime, sequence.CycleType }) }));
    Console.WriteLine($"PASS owned LoveTester models, {sampledChannels} sampled channels, all page sequences, XML and dynamic digit/control textures.");
}
finally { foreach (var archive in archives) archive.Dispose(); }
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static void Reject(Action action, string message)
{
    try { action(); } catch (NotSupportedException) { return; }
    throw new InvalidDataException(message);
}
