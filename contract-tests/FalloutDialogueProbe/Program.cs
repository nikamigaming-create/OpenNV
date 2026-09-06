using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

IdleAnimationProbe.Run();

ActorPackageCommandProbe.Exercise();
IdleCollectionProbe.Run();
IdleConditionProbe.Run();
HudDeclarationsProbe.Run();
MessageMenuDeclarationsProbe.Run();
ActorFaceAnimationProbe.Run();

if (args is ["--audit-message-declarations", var messageExecutable])
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        command = "ShowMessage",
        defaultButton = FalloutExecutableStringTable.ReadShowMessageDefaultButton(messageExecutable),
    }));
    return;
}

if (args is ["--audit-hud-declarations", var hudExecutable])
{
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(FalloutExecutableStringTable.ReadHudMessageDeclarations(hudExecutable)));
    return;
}

if (args is ["--inspect-float-defaults", var floatExecutable, var floatPrefix])
{
    foreach (var (key, value) in FalloutExecutableStringTable.ReadFloatDefaults(floatExecutable).Where(value => value.Key.StartsWith(floatPrefix, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { key, value }));
    return;
}

if (args is ["--inspect-defaults", var executable, var prefix])
{
    foreach (var (key, value) in FalloutExecutableStringTable.Read(executable).Where(value => value.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { key, value }));
    return;
}

if (args is ["--inspect-menu", var menuRoot, var menuPath])
{
    RuntimeLiveContentSource.Configure(menuRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var source = RuntimeLiveContentSource.Current!;
    Console.WriteLine(FalloutMenuXml.Expand(FalloutMenuXml.Read(menuPath)));
    return;
}

if (args is ["--inspect-tri", var triRoot, var triPath])
{
    RuntimeLiveContentSource.Configure(triRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var source = RuntimeLiveContentSource.Current!;
    if (!source.TryRead(triPath, null, out var bytes, out var identity)) throw new FileNotFoundException("Owned TRI is missing.", triPath);
    var tri = OpenNV.Runtime.Formats.FaceGen.FalloutTriFile.Read(bytes);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        identity, tri.VertexCount, totalVertices = tri.Vertices.Length, faces = tri.Faces.Length,
        deltas = tri.DeltaMorphs.Select(morph => new { morph.Name, morph.Scale }),
        stat = tri.StatMorphs.Select(morph => new { morph.Name, morph.AddedVertexStart, count = morph.VertexIndices.Length }),
    }));
    return;
}

if (args is ["--inspect-font", var fontRoot, var fontId, var fontText])
{
    RuntimeLiveContentSource.Configure(fontRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var source = RuntimeLiveContentSource.Current!;
    var settings = FalloutInstallationSettings.Read(source);
    var path = settings.Require("Fonts", "sFontFile_" + int.Parse(fontId, System.Globalization.CultureInfo.InvariantCulture));
    if (!source.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Owned font is missing.", path);
    var font = OpenNV.Runtime.Formats.Gamebryo.FalloutBitmapFont.Read(bytes);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        path, font.SourceSize, font.Height, font.Ascent, font.TileBaseline, width = font.Measure(fontText),
        glyphs = fontText.Distinct().Select(character => new { character, glyph = font.Glyph(character), advance = font.Glyph(character).Advance }),
    }));
    return;
}

if (args is ["--inspect-furniture-selection", var aiRoot, var npcEditorId, var markerText])
{
    RuntimeLiveContentSource.Configure(aiRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var npc = FalloutDialogueTopic.Find(records, "NPC_", npcEditorId);
    var quests = new FalloutQuestState(records);
    var marker = int.Parse(markerText, System.Globalization.CultureInfo.InvariantCulture);
    var traits = FalloutAiPackages.TemplateOwner(records, npc, 1);
    var female = (BinaryPrimitives.ReadUInt32LittleEndian(traits.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span) & 1) != 0;
    var model = FalloutAiPackages.TemplateOwner(records, npc, 64);
    var skeleton = "meshes/" + FalloutDialogueTopic.Text(model.ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
    var sitting = 0;
    FalloutFormKey? furniture = null;
    float Evaluate(FalloutCondition condition) => condition.Function switch
    {
        58 or 59 or 79 or 546 => quests.Evaluate(condition),
        70 => (female ? 1u : 0u) == condition.Argument1 ? 1 : 0,
        72 => condition.FormArgument1 == npc.FormKey ? 1 : 0,
        101 or 182 or 247 or 392 => 0,
        159 => sitting,
        160 => marker,
        163 => furniture == condition.FormArgument1 ? 1 : 0,
        _ => throw new NotSupportedException($"Audit context does not bind {condition.Owner.FormKey}/{condition.Function}."),
    };
    var package = FalloutAiPackages.Select(records, npc.FormKey, Evaluate);
    Require(package is not null, "Owned NPC has no initial selected package.");
    var location = package!.ReadSubrecords().Single(field => field.Signature == "PLDT").Data;
    var reference = records.GetEffective(package.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(location.Span[4..])));
    furniture = FalloutDialogueTopic.RequiredForm(reference, "NAME");
    sitting = 1;
    var selected = new FalloutFurnitureIdleTree(records, skeleton).Select(Evaluate);
    var idle = FalloutActorIdleSource.Resolve(records, selected);
    Console.WriteLine($"OPENNV_OWNED_FURNITURE_SELECTION npc={npc.FormKey} package={package!.FormKey} marker={marker} baseIdle={idle.Form} source={idle.AnimationPath}");
    return;
}

var imageCommands = FalloutImageSpaceCommands.Read("; imod Ignored\nApplyImageSpaceModifier ExampleEffect *\nrimod ExampleEffect");
Require(imageCommands.Count == 2 && imageCommands[0].Apply && !imageCommands[1].Apply && imageCommands[0].EditorId == "ExampleEffect",
    "Source modifier command order, aliases or annotation changed behavior.");
var conditionalImageRejected = false;
try { FalloutImageSpaceCommands.Read("if UnknownCondition\nimod ExampleEffect\nendif"); }
catch (NotSupportedException) { conditionalImageRejected = true; }
Require(conditionalImageRejected, "An unevaluated condition applied an image-space modifier.");

Require(FalloutGameSettingStrings.ReadUnpooledDefault(Encoding.ASCII.GetBytes("sProbeChoice\0\0Sample choice.\0"), "sProbeChoice") == "Sample choice.",
    "Unpooled default string lost its owned literal.");
var pooledRejected = false;
try { FalloutGameSettingStrings.ReadUnpooledDefault(Encoding.ASCII.GetBytes("sProbeChoice\0sNextSetting\0"), "sProbeChoice"); }
catch (NotSupportedException) { pooledRejected = true; }
Require(pooledRejected, "A pooled default was replaced with another setting's name.");

var initializer = new byte[23];
new byte[] { 0x55, 0x8b, 0xec, 0x68 }.CopyTo(initializer, 0);
BinaryPrimitives.WriteUInt32LittleEndian(initializer.AsSpan(4), 0x200);
initializer[8] = 0x68;
BinaryPrimitives.WriteUInt32LittleEndian(initializer.AsSpan(9), 0x100);
initializer[13] = 0xb9;
BinaryPrimitives.WriteUInt32LittleEndian(initializer.AsSpan(14), 0x300);
initializer[18] = 0xe8;
string? SourceLiteral(uint pointer) => pointer switch { 0x100 => "sProbePooled", 0x200 => "Shared\nsource literal", _ => null };
var initialized = FalloutExecutableStringTable.ReadInitializers(initializer, SourceLiteral, address => address == 0x300);
Require(initialized["sProbePooled"] == "Shared\nsource literal", "Pooled default lost its compiler-owned association or newline.");
Require(FalloutExecutableStringTable.ReadInitializers(initializer, SourceLiteral, _ => false).Count == 0,
    "A non-object code operand was admitted as a setting descriptor.");
Require(FalloutExecutableStringTable.ReadInitializers(initializer.AsSpan(0, 22), SourceLiteral, _ => true).Count == 0,
    "A truncated initializer was admitted.");
var ambiguousDefaultRejected = false;
try { FalloutExecutableStringTable.ReadInitializers(initializer.Concat(initializer).ToArray(), SourceLiteral, _ => true); }
catch (InvalidDataException) { ambiguousDefaultRejected = true; }
Require(ambiguousDefaultRejected, "Ambiguous setting initializers silently chose one source.");
var integerInitializer = initializer.ToArray();
BinaryPrimitives.WriteUInt32LittleEndian(integerInitializer.AsSpan(4), 0xff12a0e7);
var integerDefaults = FalloutExecutableStringTable.ReadIntegerInitializers(integerInitializer,
    address => address == 0x100 ? "iArbitraryInteger" : null, address => address == 0x300);
Require(integerDefaults["iArbitraryInteger"] == 0xff12a0e7, "Integer default lost its immediate source bits.");
Require(FalloutExecutableStringTable.ReadIntegerInitializers(integerInitializer.AsSpan(0, 22), _ => "iProbe", _ => true).Count == 0 &&
    FalloutExecutableStringTable.ReadIntegerInitializers(integerInitializer, _ => "iProbe", _ => false).Count == 0,
    "Truncated or non-object integer initializers were admitted.");
var ambiguousIntegerRejected = false;
try { FalloutExecutableStringTable.ReadIntegerInitializers(integerInitializer.Concat(integerInitializer).ToArray(), _ => "iProbe", _ => true); }
catch (InvalidDataException) { ambiguousIntegerRejected = true; }
Require(ambiguousIntegerRejected, "Ambiguous integer defaults silently chose one source.");
var floatInitializer = new byte[28];
new byte[] { 0x55, 0x8b, 0xec, 0x51, 0xd9, 0x05 }.CopyTo(floatInitializer, 0);
BinaryPrimitives.WriteUInt32LittleEndian(floatInitializer.AsSpan(6), 0x200);
new byte[] { 0xd9, 0x1c, 0x24, 0x68 }.CopyTo(floatInitializer, 10);
BinaryPrimitives.WriteUInt32LittleEndian(floatInitializer.AsSpan(14), 0x100);
floatInitializer[18] = 0xb9;
BinaryPrimitives.WriteUInt32LittleEndian(floatInitializer.AsSpan(19), 0x300);
floatInitializer[23] = 0xe8;
var floatDefaults = FalloutExecutableStringTable.ReadFloatInitializers(floatInitializer,
    address => address == 0x100 ? "fProbe:Interface" : null, address => address == 0x300,
    address => address == 0x200 ? BitConverter.Int32BitsToSingle(unchecked((int)0xbeaaaaab)) : throw new Exception("Wrong constant source."));
Require(BitConverter.SingleToInt32Bits(floatDefaults["fProbe:Interface"]) == unchecked((int)0xbeaaaaab),
    "Float default lost its source bits or descriptor association.");
Require(FalloutExecutableStringTable.ReadFloatInitializers(floatInitializer.AsSpan(0, 27), _ => "fProbe", _ => true, _ => 1).Count == 0,
    "Truncated float initializer was admitted.");
Require(FalloutExecutableStringTable.ReadFloatInitializers(floatInitializer, _ => "fProbe", _ => false, _ => 1).Count == 0,
    "Float default without a writable descriptor was admitted.");
var badFloatRejected = false;
try { FalloutExecutableStringTable.ReadFloatInitializers(floatInitializer, _ => "fProbe", _ => true, _ => float.NaN); }
catch (InvalidDataException) { badFloatRejected = true; }
Require(badFloatRejected, "Non-finite float default was admitted.");
var chained = new byte[8];
BinaryPrimitives.WriteUInt32LittleEndian(chained, 0x11223344);
BinaryPrimitives.WriteUInt32LittleEndian(chained.AsSpan(4), 0x55667788);
var chainedTail = FalloutExecutableStringTable.DecodeChain(chained, 0xaabbccdd);
Require(chainedTail == 0x55667788 && BinaryPrimitives.ReadUInt32LittleEndian(chained) == 0xbb99ff99 &&
    BinaryPrimitives.ReadUInt32LittleEndian(chained.AsSpan(4)) == 0x444444cc, "Rolling source decoding lost the previous ciphertext word.");

if (args is ["--inspect-setting", var settingRoot, var settingName])
{
    RuntimeLiveContentSource.Configure(settingRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var source = RuntimeLiveContentSource.Current!;
    using var stack = FalloutPluginStack.Load(source.PluginSources);
    Console.WriteLine($"OPENNV_OWNED_STRING_DEFAULT name={settingName} value={FalloutGameSettingStrings.Read(stack, settingName)}");
    return;
}

var centered = System.Xml.Linq.XElement.Parse("<x><copy src='screen()' trait='width'/><sub src='me()' trait='width'/><div>2</div></x>");
var tileFont = new OpenNV.Runtime.Formats.Gamebryo.FalloutBitmapFont(20, "synthetic", new[]
{
    new OpenNV.Runtime.Formats.Gamebryo.FalloutBitmapGlyph(0, 0, 0, 1, 1, 10, 18, 0, 0, 17),
    new OpenNV.Runtime.Formats.Gamebryo.FalloutBitmapGlyph(0, 0, 0, 1, 1, 10, 14, 0, 0, 9),
});
Require(tileFont.TileBaseline == 10 && tileFont.Ascent == 17,
    "Tile drawing reused maximum ascent instead of the independent source descent extent.");
Require(FalloutMenuXml.Number(centered, (source, _) => source == "screen()" ? 1600 : 600) == 500,
    "Menu geometry did not follow its declared relative expression.");
var conditional = System.Xml.Linq.XElement.Parse("<value><copy>7</copy><onlyif><copy>0</copy></onlyif><add>2</add></value>");
Require(FalloutMenuXml.Number(conditional, (_, _) => throw new InvalidOperationException()) == 2,
    "Menu conditional arithmetic ignored a source predicate.");
var indexed = System.Xml.Linq.XElement.Parse("<brightness><copy>1</copy><copy src='me()' trait='_brightness_'/><div>2</div></brightness>");
Require(FalloutMenuXml.Number(indexed, (source, trait) => source == "me()" && trait == "_brightness_1" ? 240 :
    throw new InvalidOperationException("Indexed source trait lost its accumulated selector.")) == 120,
    "Menu indexed traits did not select the source property before continuing arithmetic.");
var fractionalIndexRejected = false;
try { FalloutMenuXml.Number(System.Xml.Linq.XElement.Parse("<value><copy>0.5</copy><copy src='me()' trait='_choice_'/></value>"), (_, _) => 0); }
catch (InvalidDataException) { fractionalIndexRejected = true; }
Require(fractionalIndexRejected, "A fractional menu selector silently resolved a different source trait.");
var unsupportedMenuOperator = false;
try { FalloutMenuXml.Number(System.Xml.Linq.XElement.Parse("<value><unknown>3</unknown></value>"), (_, _) => 0); }
catch (NotSupportedException) { unsupportedMenuOperator = true; }
Require(unsupportedMenuOperator, "An unknown source menu operator was accepted.");

if (args is ["--inspect-package", var packageRoot, var packageEditorId])
{
    RuntimeLiveContentSource.Configure(packageRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var package = FalloutScriptPackage.Read(FalloutDialogueTopic.Find(records, "PACK", packageEditorId));
    Console.WriteLine($"OPENNV_OWNED_PACKAGE source={package.Form} sequence={package.RunInSequence} once={package.DoOnce} timer={package.IdleTimer:R}");
    foreach (var idle in package.Idles) Console.WriteLine($"IDLE {idle}");
    foreach (var entry in package.Events) Console.WriteLine($"EVENT {entry.Key} {entry.Value}");
    return;
}

if (args is ["--inspect-idle", var idleRoot, var idleEditorId])
{
    RuntimeLiveContentSource.Configure(idleRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var source = FalloutActorIdleSource.Resolve(records, idleEditorId);
    Console.WriteLine($"IDLE {source.Form} {source.AnimationPath}");
    foreach (var item in source.Objects) Console.WriteLine($"ANIO {item.Form} {item.ModelPath}");
    return;
}

if (args is ["--inspect-record", var recordRoot, var signature, var editorId])
{
    RuntimeLiveContentSource.Configure(recordRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var records = FalloutPluginStack.Load(content.PluginSources);
    var identityParts = editorId.Split(':');
    var record = identityParts.Length == 2
        ? records.GetEffective(new(identityParts[0], uint.Parse(identityParts[1], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)))
        : FalloutDialogueTopic.Find(records, signature, editorId);
    Require(record.Signature == signature, "Requested owned record has a different signature.");
    foreach (var field in record.ReadSubrecords())
        Console.WriteLine($"{field.Signature} {field.Data.Length}: " +
            (field.Signature is "EDID" or "MODL" or "SCTX" ? Encoding.UTF8.GetString(field.Data.Span).TrimEnd('\0') : Convert.ToHexString(field.Data.Span)));
    return;
}

if (args is ["--inspect-stages", var inspectRoot, var inspectQuest])
{
    RuntimeLiveContentSource.Configure(inspectRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var content = RuntimeLiveContentSource.Current!;
    using var inspectStack = FalloutPluginStack.Load(content.PluginSources);
    var quest = FalloutDialogueTopic.Find(inspectStack, "QUST", inspectQuest);
    short? stage = null;
    foreach (var field in quest.ReadSubrecords())
    {
        if (field.Signature == "INDX") stage = BinaryPrimitives.ReadInt16LittleEndian(field.Data.Span);
        if (field.Signature == "SCTX") Console.WriteLine($"STAGE {stage}\n{Encoding.UTF8.GetString(field.Data.Span).TrimEnd('\0')}");
    }
    return;
}

var directory = Path.Combine(Path.GetTempPath(), "OpenNV-dialogue-" + Guid.NewGuid().ToString("N"));
Require(FalloutActorIdleCommands.Read("; Ignored.PlayIdle Fake\nActorA.PlayIdle IdleA ; comment\nActorB.playidle IdleB")
    .SequenceEqual(new[] { new FalloutActorIdleCommand("ActorA", "IdleA"), new FalloutActorIdleCommand("ActorB", "IdleB") }),
    "PlayIdle lost source targets or command order.");
Directory.CreateDirectory(directory);
try
{
    var header = new byte[12];
    BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 3);
    var topic = Record("DIAL", 0x800, Field("EDID", Encoding.ASCII.GetBytes("SyntheticTopic\0")));
    // The first INFO has a larger ID: sorting by ID would pick the wrong line.
    var first = Info(0x901, "First", 0x900, flags: 5);
    var second = Info(0x811, "Second", 0x900);
    var group = new byte[24 + first.Length + second.Length];
    Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 7);
    first.CopyTo(group, 24); second.CopyTo(group, 24 + first.Length);
    var idleRecord = Record("IDLE", 0xa00, Field("EDID", Encoding.ASCII.GetBytes("SourceIdle\0"))
        .Concat(Field("MODL", Encoding.ASCII.GetBytes("animations/source.kf\0"))).ToArray());
    var animationObject = Record("ANIO", 0xa01, Field("DATA", BitConverter.GetBytes(0xa00u))
        .Concat(Field("MODL", Encoding.ASCII.GetBytes("objects/source.nif\0"))).ToArray());
    var packageData = new byte[12]; packageData[4] = 6;
    var packageLocation = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(packageLocation, 3);
    var packageRecord = Record("PACK", 0xb00, Field("EDID", Encoding.ASCII.GetBytes("ProbePackage\0"))
        .Concat(Field("PKDT", packageData)).Concat(Field("PLDT", packageLocation))
        .Concat(Field("IDLF", [5])).Concat(Field("IDLC", [1])).Concat(Field("IDLT", BitConverter.GetBytes(0.75f)))
        .Concat(Field("IDLA", BitConverter.GetBytes(0xa00u))).Concat(Field("POBA", []))
        .Concat(Field("INAM", BitConverter.GetBytes(0xa00u))).ToArray());
    var stringSetting = Record("GMST", 0xc00, Field("EDID", Encoding.ASCII.GetBytes("sProbeChoice\0"))
        .Concat(Field("DATA", Encoding.ASCII.GetBytes("Plugin choice.\0"))).ToArray());
    var bytes = Record("TES4", 0, Field("HEDR", header)).Concat(topic).Concat(group)
        .Concat(idleRecord).Concat(animationObject).Concat(packageRecord).Concat(stringSetting).Concat(Record("ANIO", 0xa02, [])).ToArray();
    File.WriteAllBytes(Path.Combine(directory, "Synthetic.esm"), bytes);
    using var stack = FalloutPluginStack.Load(directory, ["Synthetic.esm"]);
    Require(FalloutGameSettingStrings.Read(stack, "sProbeChoice") == "Plugin choice.", "A winning GMST did not override the engine default.");
    var package = FalloutScriptPackage.Read(FalloutDialogueTopic.Find(stack, "PACK", "ProbePackage"));
    Require(package.RunInSequence && package.DoOnce && package.IdleTimer == 0.75f &&
        package.Events["POBA"] == package.Idles.Single(), "Package source timing/events differ.");
    var commands = FalloutScriptPackageCommands.Read("; player.addscriptpackage Ignore\nplayer.AddScriptPackage ProbePackage\nplayer.RemoveScriptPackage");
    Require(commands.Count == 2 && commands[0].PackageEditorId == "ProbePackage" && commands[1].PackageEditorId is null,
        "Package commands lost order, comments or removal.");
    var conditionalRejected = false;
    try { FalloutScriptPackageCommands.Read("if SomeCondition\nplayer.AddScriptPackage ProbePackage\nendif"); }
    catch (NotSupportedException) { conditionalRejected = true; }
    Require(conditionalRejected, "An unevaluated condition executed a package.");
    var selectedIdle = FalloutActorIdleSource.Resolve(stack, "SourceIdle");
    Require(selectedIdle.AnimationPath == "meshes/animations/source.kf" && selectedIdle.Objects.Count == 1 &&
        selectedIdle.Objects[0].Form == new FalloutFormKey("Synthetic.esm", 0xa01) &&
        selectedIdle.Objects[0].ModelPath == "meshes/objects/source.nif", "ANIO was not resolved from its source IDLE link.");
    var source = FalloutDialogueTopic.Read(stack, "SyntheticTopic");
    var said = new HashSet<FalloutFormKey>();
    var speaker = new FalloutFormKey("Synthetic.esm", 0x900);
    var selected = source.Select(speaker, said, _ => throw new Exception("Unexpected quest condition."));
    Require(selected?.Responses[0].Text == "First", "INFO source order was replaced with FormID order.");
    Require(selected!.Flags == 5, "SayTo rejected or erased the authored Goodbye and Say Once flags.");
    said.Add(selected!.Record.FormKey);
    Require(source.Select(speaker, said, _ => 0)?.Responses[0].Text == "Second", "Say Once did not advance selection.");
    Require(source.Select(new FalloutFormKey("Synthetic.esm", 0x999), said, _ => 0) is null, "GetIsID ignored source speaker.");
    Require(FalloutDialogueTopic.SayToCommands("; nobody.SayTo player Fake\nSpeaker.SayTo player Topic ; comment").Single() ==
        new FalloutSayToCommand("Speaker", "player", "Topic"), "Source SayTo parsing differs.");
    var randomBytes = bytes.ToArray();
    var flagOffset = randomBytes.AsSpan().IndexOf(Field("DATA", [1, 0, 5, 0]));
    Require(flagOffset >= 0, "Synthetic INFO flags were not found.");
    randomBytes[flagOffset + 8] |= 2;
    File.WriteAllBytes(Path.Combine(directory, "Random.esm"), randomBytes);
    using var random = FalloutPluginStack.Load(directory, ["Random.esm"]);
    var randomRejected = false;
    try { FalloutDialogueTopic.Read(random, "SyntheticTopic").Select(new FalloutFormKey("Random.esm", 0x900), new HashSet<FalloutFormKey>(), _ => 0); }
    catch (NotSupportedException) { randomRejected = true; }
    Require(randomRejected, "Goodbye admission also admitted unowned Random selection.");
    Require(FalloutDialogueTopic.ScriptText("set value to 3"u8) == "set value to 3" &&
        FalloutDialogueTopic.ScriptText("set value to 3\0"u8) == "set value to 3" &&
        FalloutDialogueTopic.ScriptText([]) == "", "SCTX byte extents were treated as mandatory null-terminated names.");
    var embeddedNullRejected = false;
    try { FalloutDialogueTopic.ScriptText("one\0two"u8); }
    catch (InvalidDataException) { embeddedNullRejected = true; }
    Require(embeddedNullRejected, "An embedded source-script null was admitted.");
    byte[] EventPackage(uint id, byte[] scriptBytes) => Record("PACK", id,
        Field("EDID", Encoding.ASCII.GetBytes("EventPackage" + id + "\0"))
            .Concat(Field("PKDT", packageData)).Concat(Field("PLDT", packageLocation))
            .Concat(Field("POCA", [])).Concat(Field("SCTX", scriptBytes)).ToArray());
    File.WriteAllBytes(Path.Combine(directory, "EventScripts.esm"), Record("TES4", 0, Field("HEDR", header))
        .Concat(EventPackage(0xd00, "; comment only"u8.ToArray()))
        .Concat(EventPackage(0xd01, "UnownedCommand"u8.ToArray())).ToArray());
    using var eventStack = FalloutPluginStack.Load(directory, ["EventScripts.esm"]);
    var commentPackage = FalloutScriptPackage.Read(eventStack.GetEffective(new("EventScripts.esm", 0xd00)));
    var scriptedPackage = FalloutScriptPackage.Read(eventStack.GetEffective(new("EventScripts.esm", 0xd01)));
    var eventOrder = new List<string>();
    var lifecycle = new FalloutPackageEvents((owner, kind) =>
    {
        eventOrder.Add($"{owner.Form.ObjectId:x}:{kind}");
        owner.EventPrograms.GetValueOrDefault(kind)?.RequireEmptyScript();
    });
    lifecycle.Change(commentPackage);
    lifecycle.Change(commentPackage);
    lifecycle.Complete();
    lifecycle.Complete();
    lifecycle.Change(scriptedPackage);
    Require(eventOrder.SequenceEqual(new[] { "d00:POBA", "d00:POEA", "d00:POCA", "d01:POBA" }),
        "Package begin/completion/change events repeated or executed before their lifecycle boundary.");
    Require(lifecycle.Active == scriptedPackage && !lifecycle.Done && lifecycle.Error is null,
        "An unreached package-change program prevented package admission.");
    var eventRejected = false;
    try { lifecycle.Change(null); }
    catch (NotSupportedException error) { eventRejected = error.Message.Contains("POCA", StringComparison.Ordinal); }
    Require(eventRejected && lifecycle.Active == scriptedPackage && lifecycle.Error is not null,
        "A reached package script was admitted without an execution owner or advanced past its failure.");
    var attempts = eventOrder.Count;
    try { lifecycle.Change(null); }
    catch (NotSupportedException) { }
    Require(eventOrder.Count == attempts, "A failed package event was silently replayed.");
    var needle = Field("CTDA", Condition(0x900));
    var start = bytes.AsSpan().IndexOf(needle);
    Require(start >= 0, "Synthetic CTDA was not found.");
    var targetBytes = bytes.ToArray();
    BinaryPrimitives.WriteUInt16LittleEndian(targetBytes.AsSpan(start + 14), 70);
    BinaryPrimitives.WriteUInt32LittleEndian(targetBytes.AsSpan(start + 18), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(targetBytes.AsSpan(start + 26), 1);
    File.WriteAllBytes(Path.Combine(directory, "Target.esm"), targetBytes);
    using var targetStack = FalloutPluginStack.Load(directory, ["Target.esm"]);
    var targetTopic = FalloutDialogueTopic.Read(targetStack, "SyntheticTopic");
    var targetSpeaker = new FalloutFormKey("Target.esm", 0x900);
    var targetRejected = false;
    try { targetTopic.Select(targetSpeaker, new HashSet<FalloutFormKey>(), _ => 0); }
    catch (NotSupportedException) { targetRejected = true; }
    Require(targetRejected, "A target condition executed without a target state owner.");
    var contextCalls = 0;
    var targetInfo = targetTopic.Select(targetSpeaker, new HashSet<FalloutFormKey>(), _ => 0, condition =>
    {
        Require(condition.Function == 70 && condition.RunOn == 1 && condition.Argument1 == 0,
            "Dialogue lost the target condition's function or arguments.");
        contextCalls++; return 1;
    });
    Require(targetInfo?.Responses[0].Text == "First" && contextCalls == 1,
        "The bound target condition did not select the authored INFO.");
    Require(targetTopic.Select(targetSpeaker, new HashSet<FalloutFormKey>(), _ => 0, _ => 0)?.Responses[0].Text == "Second",
        "A false target condition did not preserve source INFO selection order.");
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(start + 6 + 8), 999);
    var unknownPath = Path.Combine(directory, "Unknown.esm");
    File.WriteAllBytes(unknownPath, bytes);
    using var unknown = FalloutPluginStack.Load(directory, ["Unknown.esm"]);
    var rejected = false;
    try { FalloutDialogueTopic.Read(unknown, "SyntheticTopic").Select(new FalloutFormKey("Unknown.esm", 0x900), new HashSet<FalloutFormKey>(), _ => 0); }
    catch (NotSupportedException) { rejected = true; }
    Require(rejected, "Unknown CTDA silently selected dialogue.");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine("OPENNV_DIALOGUE_SOURCE_OK fileOrder=true sayOnce=true speakerCondition=true unknownVisible=true");

if (args is [var dataRoot])
{
    RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
    using var source = RuntimeLiveContentSource.Current!;
    using var stack = FalloutPluginStack.Load(source.PluginSources);
    var topic = FalloutDialogueTopic.Read(stack, "VCG01Intro");
    var speaker = FalloutDialogueTopic.Find(stack, "ACHR", "DocMitchellREF");
    var npcKey = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
    var said = new HashSet<FalloutFormKey>();
    var info = topic.Select(npcKey, said, _ => throw new InvalidOperationException("Unexpected quest condition."))!;
    var voiceType = stack.GetEffective(FalloutDialogueTopic.RequiredForm(stack.GetEffective(npcKey), "VTCK"));
    var voiceName = FalloutDialogueTopic.Text(voiceType.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span);
    var paths = source.ResourcePathsUnder($"sound/voice/{info.Record.Plugin.Name}/{voiceName}");
    foreach (var response in info.Responses)
    {
        var suffix = $"_{info.Record.FormKey.ObjectId:x8}_{response.Number}.ogg";
        var voice = paths.Single(path => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        Require(source.TryRead(voice, null, out var voiceBytes, out _) && voiceBytes.AsSpan(0, 4).SequenceEqual("OggS"u8), "Owned voice missing.");
        Require(source.TryRead(Path.ChangeExtension(voice, ".lip"), null, out var lip, out _) && lip.Length > 12, "Owned LIP missing.");
    }
    Console.WriteLine($"OPENNV_OWNED_DIALOGUE_OK plugins={stack.Plugins.Count} infos={topic.Infos.Count} selected={info.Record.FormKey} responses={info.Responses.Count} endScript={info.EndScript.Trim()} voiceAndLip=owned-memory");
}

static byte[] Info(uint id, string text, uint npc, byte flags = 4)
{
    var response = new byte[24]; response[12] = 1; response[13] = 0xff;
    return Record("INFO", id, Field("DATA", [1, 0, flags, 0]).Concat(Field("QSTI", BitConverter.GetBytes(0x700u)))
        .Concat(Field("TRDT", response)).Concat(Field("NAM1", Encoding.ASCII.GetBytes(text + "\0")))
        .Concat(Field("CTDA", Condition(npc))).Concat(Field("NEXT", []))
        .Concat(Field("SCTX", Encoding.ASCII.GetBytes("SetStage SyntheticQuest 5\0"))).ToArray());
}
static byte[] Condition(uint npc)
{
    var data = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 72); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), npc);
    return data;
}
static byte[] Field(string signature, byte[] data)
{
    var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
}
static byte[] Record(string signature, uint id, byte[] data)
{
    var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
