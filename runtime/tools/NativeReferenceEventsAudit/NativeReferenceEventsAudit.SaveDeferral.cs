using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void SaveDeferral()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-save-deferral-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Contact.esm");
        var player = new RuntimeNativePlayer();
        player.Configure(RuntimeConfiguration.Load(), Transform3D.Identity, FalloutCameraProjection.FromReferenceFov(75, 1));
        AddChild(player); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
        try { VerifySaveDeferral(path, player); }
        finally
        {
            player.QueueFree();
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void VerifySaveDeferral(string path, RuntimeNativePlayer player)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var referenceBytes = Record("REFR", 0x901, Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", new byte[24]));
        var group = new byte[24 + referenceBytes.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
        BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); referenceBytes.CopyTo(group, 24);
        var playerStats = new byte[24]; playerStats[8] = 1;
        File.WriteAllBytes(path, Record("TES4", 0, Field("HEDR", header)).Concat(Record("ACTI", 0x700))
            .Concat(Record("NPC_", 7, Field("ACBS", playerStats), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5])))
            .Concat(Record("CELL", 0x800, Field("DATA", [1]))).Concat(group).ToArray());
        using var records = FalloutPluginStack.Load(Path.GetDirectoryName(path)!, ["Contact.esm"]);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var inventory = new FalloutPlayerInventory();
        var scripts = new FalloutQuestScripts(records, new(records), new HashSet<FalloutFormKey>(), inventory,
            defaultProcessingDelay: .1f, references: world);
        var driver = new NativeSaveDeferralDriverFixture();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = typeof(RuntimeNativeOpeningStageDriver);
        void Bind(string field, object value) => owner.GetField(field, flags)!.SetValue(driver, value);
        try
        {
            Bind("_player", player); Bind("_scripts", scripts);
            var reference = world.Get(Key(0x901));
            reference.ProcedureCaptureBlocker = "First native procedure snapshot is pending.";
            var source = records.GetEffective(Key(0x700));
            try
            {
                driver.ApplyNativeSourceCommand(source.FormKey, new(records, source, source, []), "AutoSave", []);
                throw new InvalidDataException("A diagnostic command fabricated an entered source AutoSave instruction.");
            }
            catch (InvalidOperationException error) when (error.Message == "Source save requires an actual entered instruction.")
            { }
            Require(scripts.ScriptManualSaves.Order.Requests.Count == 0 && world.PendingProcedureCaptureCount == 1 &&
                reference.ProcedureCaptureBlocker == "First native procedure snapshot is pending.",
                "A rejected source save created a request or consumed its independent procedure capture blocker.");
            reference.ProcedureCaptureBlocker = null;
            Require(scripts.ScriptManualSaves.Order.Requests.Count == 0 && world.PendingProcedureCaptureCount == 0 && world.Capture().Count > 0,
                "Settling the independent procedure capture fabricated a source autosave request.");
            GD.Print("OPENNV_NATIVE_SAVE_DEFERRAL_PASS absentInstructionRefused=true queueUnchanged=true independentProcedureBlocker=true " +
                "settledCapture=true actualSourceAutoSave=unexecuted");
        }
        finally { driver.Free(); }
    }
}

internal partial class NativeSaveDeferralDriverFixture : RuntimeNativeOpeningStageDriver
{
    public override void _Ready() { }
}
