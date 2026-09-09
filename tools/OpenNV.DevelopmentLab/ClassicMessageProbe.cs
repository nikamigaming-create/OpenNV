using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;

internal static class ClassicMessageProbe
{
    internal static void Run()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["scripts/scripts.lst"] = Encoding.Latin1.GetBytes("first.int\r\nsecond.int\r\n"),
            ["text/english/dialog/first.msg"] = Encoding.Latin1.GetBytes("# source fixture\r\n{100}{}{First line\r\nsecond line}\r\n{101}{}{Door response}"),
            ["text/english/dialog/second.msg"] = Encoding.Latin1.GetBytes("{100}{}{Different list}"),
        };
        ClassicNativeScriptMessages Open() => new(path => files[path]);
        var messages = Open();
        var handle = messages.Handle(1, 100);
        if (handle == messages.Handle(2, 100) || messages.Resolve(handle, null, null).Text != "First line\nsecond line" ||
            Open().Resolve(handle, null, null).Text != "First line\nsecond line")
            throw new InvalidOperationException("Script message identity, multiline text or cold handle resolution drifted.");
        Reject(() => messages.Handle(1, 999));
        Reject(() => messages.Handle(3, 100));
        Reject(() => messages.Handle(0, 100));
        Reject(() => messages.Resolve(int.MinValue, null, null));

        var variables = new Dictionary<int, int>();
        var source = new ClassicIntProcedureState(variables, new Dictionary<int, int>(), new Dictionary<int, int>(),
            new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        ClassicIntExpressionContext Context() => new(source.ProgramVariables, source.LocalVariables, source.ScriptLocalVariables,
            source.MapVariables, source.GlobalVariables, 1, 2, null, null, new Dictionary<(int, int), int>(),
            new Dictionary<(int, int), int>(), new Dictionary<int, int>(), new Dictionary<(int, int), int>(),
            new Dictionary<string, int>(), null, null, null, new ClassicIntObjectHandleTable(new Dictionary<ClassicIntObjectCreation, int>()), MessageSource: Open());
        var fetch = Program((0xc001, 1), (0xc001, 100), (0x8105, null), (0xc001, 0), (0x8013, null), (0x801c, null));
        var result = ClassicIntProcedureVm.Execute(fetch, "event", source, Context(), ClassicIntWorldObjectState.Empty, null, 100);
        if (result.State.ProgramVariables[0] != handle || variables.Count != 0)
            throw new InvalidOperationException("Message lookup mutated source state or lost its stable handle.");
        var cold = JsonSerializer.Deserialize<ClassicIntProcedureState>(JsonSerializer.Serialize(result.State))!;
        var display = Program((0xc001, 0), (0x8012, null), (0x80b8, null), (0x801c, null));
        var displayed = ClassicIntProcedureVm.Execute(display, "event", cold, Context(), ClassicIntWorldObjectState.Empty, null, 100);
        if (displayed.MessageEffects is not [{ MessageList: 1, MessageId: 100, Text: "First line\nsecond line" }])
            throw new InvalidOperationException("Saved INT message variable did not display its owned text.");
        var literal = Program((0x9001, 4), (0x80b8, null), (0x801c, null)) with
        { StringReferences = new Dictionary<int, string> { [4] = "Source literal" } };
        var literalResult = ClassicIntProcedureVm.Execute(literal, "event", source, Context(), ClassicIntWorldObjectState.Empty, null, 100);
        if (literalResult.MessageEffects is not [{ Text: "Source literal" }])
            throw new InvalidOperationException("Literal INT display message was lost.");
        Console.WriteLine("PASS source message list joins, multiline text, missing-message rejection, literal text and cold INT message handles.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid source message was accepted.");
    }

    private static ClassicIntProgram Program(params (ushort Opcode, int? Value)[] body)
    {
        var code = body.Select((row, index) => new ClassicIntInstruction(index, row.Opcode, row.Value)).ToArray();
        var procedure = new ClassicIntProcedure("event", 0, null, code);
        return new("synthetic-messages", [procedure], new Dictionary<string, ClassicIntProcedure> { [procedure.Name] = procedure },
            code.ToDictionary(row => row.Offset), new Dictionary<int, string>(), new Dictionary<int, string>());
    }
}
