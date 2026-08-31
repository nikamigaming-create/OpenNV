using System.Globalization;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicIntInstruction(int Offset, ushort Opcode, int? Operand);

internal sealed record ClassicIntProcedure(
    string Name,
    int BodyOffset,
    int? CanonicalEpilogueOffset,
    IReadOnlyList<ClassicIntInstruction> Instructions);

internal sealed record ClassicIntProgram(
    string Identity,
    IReadOnlyDictionary<string, ClassicIntProcedure> Procedures,
    IReadOnlyDictionary<int, ClassicIntInstruction> Instructions);

internal sealed record ClassicIntProcedureState(
    IReadOnlyDictionary<int, int> ProgramVariables,
    IReadOnlyDictionary<int, int> LocalVariables,
    IReadOnlyDictionary<int, int> ScriptLocalVariables,
    IReadOnlyDictionary<int, int> MapVariables,
    IReadOnlyDictionary<int, int> GlobalVariables,
    IReadOnlyList<int> ValueStack,
    ClassicRetailRandomLifecycleState RandomState);

internal sealed record ClassicIntProcedureResult(
    ClassicIntProcedureState State,
    int ExecutedInstructions,
    int ReturnValue,
    IReadOnlyList<ClassicIntMessageEffect> MessageEffects);

internal sealed record ClassicIntMessageEffect(
    int MessageList,
    int MessageId,
    int MessageHandle);

internal static class ClassicIntProcedureVm
{
    private const ushort Jump = 0x8004;
    private const ushort Call = 0x8005;
    private const ushort AToData = 0x800C;
    private const ushort DataToA = 0x800D;
    private const ushort SwapReturn = 0x8019;
    private const ushort PopOpcode = 0x801A;
    private const ushort Return = 0x801C;
    private const ushort FetchProgram = 0x8012;
    private const ushort StoreProgram = 0x8013;
    private const ushort PushBase = 0x802B;
    private const ushort PopBase = 0x8029;
    private const ushort PopToBase = 0x802A;
    private const ushort Branch = 0x802F;
    private const ushort StoreLocal = 0x8031;
    private const ushort FetchLocal = 0x8032;
    private const ushort Equal = 0x8033;
    private const ushort NotEqual = 0x8034;
    private const ushort GreaterThanOrEqual = 0x8036;
    private const ushort LessThan = 0x8037;
    private const ushort Add = 0x8039;
    private const ushort Subtract = 0x803A;
    private const ushort Multiply = 0x803B;
    private const ushort Divide = 0x803C;
    private const ushort Modulo = 0x803D;
    private const ushort And = 0x803E;
    private const ushort Or = 0x803F;
    private const ushort BitwiseAnd = 0x8040;
    private const ushort Not = 0x8045;
    private const ushort Negate = 0x8046;
    private const ushort Random = 0x80B4;
    private const ushort DisplayMessage = 0x80B8;
    private const ushort SelfObject = 0x80BC;
    private const ushort DudeObject = 0x80BF;
    private const ushort ScriptLocal = 0x80C1;
    private const ushort SetScriptLocal = 0x80C2;
    private const ushort MapVariable = 0x80C3;
    private const ushort SetMapVariable = 0x80C4;
    private const ushort GlobalVariable = 0x80C5;
    private const ushort SetGlobalVariable = 0x80C6;
    private const ushort CritterStat = 0x80CA;
    private const ushort Metarule = 0x810B;
    private const ushort MessageString = 0x8105;
    private const ushort DifficultyLevel = 0x812A;
    private const ushort CombatDifficulty = 0x814F;
    private const ushort SfallArrayLength = 0x8231;
    private const ushort PushInteger = 0xC001;

    internal static ClassicIntProgram Parse(JsonElement inventory, string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            throw new InvalidOperationException("Classic INT program identity is empty.");
        var procedures = new Dictionary<string, ClassicIntProcedure>();
        var instructions = new Dictionary<int, ClassicIntInstruction>();
        foreach (var row in inventory.GetProperty("procedures").EnumerateArray())
        {
            var name = row.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Classic INT procedure name is empty.");
            var code = row.GetProperty("instructions").EnumerateArray().Select(item =>
            {
                var opcodeText = item.GetProperty("opcode").GetString();
                if (!ushort.TryParse(
                        opcodeText,
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out var opcode))
                    throw new InvalidOperationException(
                        $"Classic INT opcode is invalid: {identity}:{name}.");
                var operandElement = item.GetProperty("operand");
                return new ClassicIntInstruction(
                    item.GetProperty("offset").GetInt32(),
                    opcode,
                    operandElement.ValueKind == JsonValueKind.Null
                        ? null
                        : operandElement.GetInt32());
            }).ToArray();
            if (code.Length == 0 || code[0].Offset != row.GetProperty("bodyOffset").GetInt32() ||
                !procedures.TryAdd(name, new ClassicIntProcedure(
                    name,
                    code[0].Offset,
                    row.GetProperty("canonicalEpilogueOffset").ValueKind ==
                        JsonValueKind.Null
                        ? null
                        : row.GetProperty("canonicalEpilogueOffset").GetInt32(),
                    code)))
                throw new InvalidOperationException(
                    $"Classic INT procedure inventory is invalid: {identity}:{name}.");
            foreach (var instruction in code)
                if (!instructions.TryAdd(instruction.Offset, instruction) &&
                    instructions[instruction.Offset] != instruction)
                    throw new InvalidOperationException(
                        $"Classic INT instruction offset is duplicated: {identity}:" +
                        $"0x{instruction.Offset:x}.");
        }
        return new ClassicIntProgram(identity, procedures, instructions);
    }

    internal static ClassicIntProcedureResult Execute(
        ClassicIntProgram program,
        string procedure,
        ClassicIntProcedureState source,
        ClassicIntExpressionContext game,
        ClassicRetailRandomContract randomContract,
        int instructionBudget)
    {
        if (!program.Procedures.TryGetValue(procedure, out var entry) ||
            instructionBudget <= 0)
            throw new InvalidOperationException(
                $"Classic INT procedure dispatch is invalid: {program.Identity}:{procedure}.");
        var programVariables = new Dictionary<int, int>(source.ProgramVariables);
        var locals = new Dictionary<int, int>(source.LocalVariables);
        var scriptLocals = new Dictionary<int, int>(source.ScriptLocalVariables);
        var mapVariables = new Dictionary<int, int>(source.MapVariables);
        var globals = new Dictionary<int, int>(source.GlobalVariables);
        var stack = source.ValueStack.ToList();
        var bases = new Stack<int>();
        var calls = new Stack<(int Offset, ClassicIntProcedure Procedure)>();
        var random = source.RandomState;
        var messageEffects = new List<ClassicIntMessageEffect>();
        var returnValue = 0;
        var current = entry;
        var offset = entry.BodyOffset;
        var executed = 0;
        while (true)
        {
            if (current.CanonicalEpilogueOffset == offset)
            {
                var epilogueLength = ValidateCanonicalEpilogue(
                    program, procedure, current, offset);
                if (executed + epilogueLength > instructionBudget)
                    throw Failure(program, procedure, offset, "instruction-budget");
                executed += epilogueLength;
                returnValue = current.Instructions
                    .Single(row => row.Offset == offset).Operand!.Value;
                if (bases.Count != 0)
                {
                    var valueBase = bases.Pop();
                    if (stack.Count > valueBase)
                        stack.RemoveRange(valueBase, stack.Count - valueBase);
                }
                if (calls.Count == 0)
                    return Result();
                var frame = calls.Pop();
                stack.Add(returnValue);
                offset = frame.Offset;
                current = frame.Procedure;
                continue;
            }
            if (++executed > instructionBudget ||
                !program.Instructions.TryGetValue(offset, out var instruction) ||
                !current.Instructions.Any(row => row.Offset == offset))
                throw Failure(program, procedure, offset, "instruction-budget-or-target");
            var next = NextOffset(current, instruction.Offset);
            switch (instruction.Opcode)
            {
                case PushInteger:
                    stack.Add(instruction.Operand ?? throw Failure(
                        program, procedure, offset, "missing-integer-operand"));
                    break;
                case PushBase:
                    bases.Push(stack.Count);
                    break;
                case PopOpcode:
                    Pop(stack, program, procedure, offset);
                    break;
                case FetchProgram:
                    stack.Add(Read(programVariables, Pop(stack, program, procedure, offset),
                        program, procedure, offset, "program-variable"));
                    break;
                case StoreProgram:
                    Store(programVariables, stack, program, procedure, offset, true);
                    break;
                case FetchLocal:
                    stack.Add(Read(locals, Pop(stack, program, procedure, offset),
                        program, procedure, offset, "procedure-local"));
                    break;
                case StoreLocal:
                    Store(locals, stack, program, procedure, offset, true);
                    break;
                case ScriptLocal:
                    stack.Add(Read(scriptLocals, Pop(stack, program, procedure, offset),
                        program, procedure, offset, "script-local"));
                    break;
                case SetScriptLocal:
                    Store(scriptLocals, stack, program, procedure, offset, false);
                    break;
                case MapVariable:
                    stack.Add(Read(mapVariables, Pop(stack, program, procedure, offset),
                        program, procedure, offset, "map-variable"));
                    break;
                case SetMapVariable:
                    Store(mapVariables, stack, program, procedure, offset, false);
                    break;
                case GlobalVariable:
                    stack.Add(Read(globals, Pop(stack, program, procedure, offset),
                        program, procedure, offset, "global-variable"));
                    break;
                case SetGlobalVariable:
                    Store(globals, stack, program, procedure, offset, false);
                    break;
                case DudeObject:
                    stack.Add(game.DudeObject);
                    break;
                case SelfObject:
                    stack.Add(game.SelfObject);
                    break;
                case CombatDifficulty:
                    stack.Add(game.CombatDifficulty);
                    break;
                case DifficultyLevel:
                    stack.Add(game.DifficultyLevel);
                    break;
                case CritterStat:
                    {
                        var stat = Pop(stack, program, procedure, offset);
                        var obj = Pop(stack, program, procedure, offset);
                        stack.Add(Read(game.CritterStats,
                            (obj, stat),
                            program, procedure, offset, "critter-stat"));
                        break;
                    }
                case Metarule:
                    {
                        var argument = Pop(stack, program, procedure, offset);
                        var rule = Pop(stack, program, procedure, offset);
                        stack.Add(Read(game.MetaruleValues,
                            (rule, argument),
                            program, procedure, offset, "metarule"));
                        break;
                    }
                case SfallArrayLength:
                    stack.Add(Read(game.SfallArrayLengths,
                        Pop(stack, program, procedure, offset),
                        program, procedure, offset, "sfall-array"));
                    break;
                case Equal: Binary(stack, (left, right) => Bool(left == right), program, procedure, offset); break;
                case NotEqual: Binary(stack, (left, right) => Bool(left != right), program, procedure, offset); break;
                case GreaterThanOrEqual: Binary(stack, (left, right) => Bool(left >= right), program, procedure, offset); break;
                case LessThan: Binary(stack, (left, right) => Bool(left < right), program, procedure, offset); break;
                case Add: Binary(stack, (left, right) => unchecked(left + right), program, procedure, offset); break;
                case Subtract: Binary(stack, (left, right) => unchecked(left - right), program, procedure, offset); break;
                case Multiply: Binary(stack, (left, right) => unchecked(left * right), program, procedure, offset); break;
                case Divide: Binary(stack, (left, right) => left / right, program, procedure, offset); break;
                case Modulo: Binary(stack, (left, right) => left % right, program, procedure, offset); break;
                case And: Binary(stack, (left, right) => Bool(left != 0 && right != 0), program, procedure, offset); break;
                case Or: Binary(stack, (left, right) => Bool(left != 0 || right != 0), program, procedure, offset); break;
                case BitwiseAnd: Binary(stack, (left, right) => left & right, program, procedure, offset); break;
                case Not: stack.Add(Bool(Pop(stack, program, procedure, offset) == 0)); break;
                case Negate: stack.Add(unchecked(-Pop(stack, program, procedure, offset))); break;
                case Random:
                    {
                        var maximum = Pop(stack, program, procedure, offset);
                        var minimum = Pop(stack, program, procedure, offset);
                        var result = ClassicRetailRandomLifecycle.Consume(
                            random, randomContract,
                            $"int-random:{program.Identity}:{procedure}:{offset:x}",
                            $"int-procedure:{program.Identity}:{procedure}",
                            minimum, maximum);
                        random = result.State;
                        stack.Add(result.Value);
                        break;
                    }
                case MessageString:
                    {
                        var messageId = Pop(stack, program, procedure, offset);
                        var messageList = Pop(stack, program, procedure, offset);
                        stack.Add(Read(game.MessageHandles,
                            (messageList, messageId), program, procedure, offset,
                            "message"));
                        break;
                    }
                case DisplayMessage:
                    {
                        var handle = Pop(stack, program, procedure, offset);
                        var matches = game.MessageHandles
                            .Where(row => row.Value == handle).Select(row => row.Key)
                            .Take(2).ToArray();
                        if (matches.Length != 1)
                            throw Failure(program, procedure, offset,
                                "ambiguous-message-handle");
                        messageEffects.Add(new ClassicIntMessageEffect(
                            matches[0].MessageList, matches[0].MessageId, handle));
                        break;
                    }
                case Jump:
                    next = Pop(stack, program, procedure, offset);
                    break;
                case Branch:
                    {
                        var condition = Pop(stack, program, procedure, offset);
                        var target = Pop(stack, program, procedure, offset);
                        if (condition == 0)
                            next = target;
                        break;
                    }
                case Call:
                    {
                        var target = Pop(stack, program, procedure, offset);
                        var called = program.Procedures.Values.FirstOrDefault(
                            row => row.BodyOffset == target) ?? throw Failure(
                            program, procedure, offset, "call-target");
                        calls.Push((next, current));
                        current = called;
                        next = target;
                        break;
                    }
                case Return:
                    {
                        if (calls.Count == 0)
                            return Result();
                        var returned = calls.Pop();
                        next = returned.Offset;
                        current = returned.Procedure;
                        break;
                    }
                default:
                    throw Failure(program, procedure, offset,
                        $"unsupported-opcode-{instruction.Opcode:x4}");
            }
            offset = next;
        }

        ClassicIntProcedureResult Result() => new(
            new ClassicIntProcedureState(
                programVariables, locals, scriptLocals, mapVariables, globals,
                stack, random),
            executed,
            returnValue,
            messageEffects);
    }

    private static int ValidateCanonicalEpilogue(
        ClassicIntProgram program,
        string procedure,
        ClassicIntProcedure current,
        int offset)
    {
        // The compiler's procedure ABI returns a typed zero through the data/A
        // stacks, restores both saved bases, and unwinds the return stack twice.
        // Validate the complete source sequence before applying its atomic effect.
        (ushort Opcode, int? Operand)[] expected =
        [
            (PushInteger, 0), (DataToA, null), (SwapReturn, null),
            (PopToBase, null), (PopBase, null), (AToData, null),
            (Return, null), (PopToBase, null), (PopBase, null), (Return, null),
        ];
        var actual = current.Instructions.Where(row => row.Offset >= offset).ToArray();
        if (actual.Length != expected.Length || actual.Where((row, index) =>
                row.Opcode != expected[index].Opcode ||
                row.Operand != expected[index].Operand).Any())
            throw Failure(program, procedure, offset, "canonical-epilogue-abi");
        return actual.Length;
    }

    private static int NextOffset(ClassicIntProcedure procedure, int offset) =>
        procedure.Instructions.Select(row => row.Offset).Where(value => value > offset)
            .DefaultIfEmpty(-1).Min();

    private static int Pop(
        List<int> stack,
        ClassicIntProgram program,
        string procedure,
        int offset)
    {
        if (stack.Count == 0)
            throw Failure(program, procedure, offset, "stack-underflow");
        var index = stack.Count - 1;
        var value = stack[index];
        stack.RemoveAt(index);
        return value;
    }

    private static void Store(
        Dictionary<int, int> values,
        List<int> stack,
        ClassicIntProgram program,
        string procedure,
        int offset,
        bool indexOnTop)
    {
        var first = Pop(stack, program, procedure, offset);
        var second = Pop(stack, program, procedure, offset);
        values[indexOnTop ? first : second] = indexOnTop ? second : first;
    }

    private static int Read<TKey>(
        IReadOnlyDictionary<TKey, int> values,
        TKey key,
        ClassicIntProgram program,
        string procedure,
        int offset,
        string kind) where TKey : notnull =>
        values.TryGetValue(key, out var value)
            ? value
            : throw Failure(program, procedure, offset, $"missing-{kind}");

    private static void Binary(
        List<int> stack,
        Func<int, int, int> operation,
        ClassicIntProgram program,
        string procedure,
        int offset)
    {
        var right = Pop(stack, program, procedure, offset);
        var left = Pop(stack, program, procedure, offset);
        stack.Add(operation(left, right));
    }

    private static int Bool(bool value) => value ? 1 : 0;

    private static InvalidOperationException Failure(
        ClassicIntProgram program,
        string procedure,
        int offset,
        string reason) =>
        new($"Classic INT procedure failed: {program.Identity}:{procedure}:" +
            $"0x{offset:x}:{reason}.");
}
