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
    IReadOnlyList<ClassicIntProcedure> ProcedureOrder,
    IReadOnlyDictionary<string, ClassicIntProcedure> Procedures,
    IReadOnlyDictionary<int, ClassicIntInstruction> Instructions,
    IReadOnlyDictionary<int, string> IdentifierReferences,
    IReadOnlyDictionary<int, string> StringReferences);

internal sealed record ClassicIntDoorObjectState(bool Open, bool Locked);

internal sealed record ClassicIntObjectCreation(
    int Pid,
    int Tile,
    int Elevation,
    int ScriptId);

internal sealed record ClassicIntCreatedObject(
    int ObjectHandle,
    ClassicIntObjectCreation Source);

internal sealed record ClassicIntInventoryEntry(
    int OwnerHandle,
    int ObjectHandle,
    int Quantity);

internal sealed record ClassicIntMapStartOverride(
    int TileX,
    int TileY,
    int Elevation,
    int Rotation);

internal sealed record ClassicIntAttackRequest(
    int ActorHandle,
    int TargetHandle,
    IReadOnlyList<int> SourceArguments);

internal interface IClassicIntActorQueries
{
    bool CanSee(int observerHandle, int targetHandle);
}

internal sealed record ClassicIntActorQueryTable(
    IReadOnlyDictionary<(int Observer, int Target), bool> Visibility) :
    IClassicIntActorQueries
{
    public bool CanSee(int observerHandle, int targetHandle) =>
        Visibility.TryGetValue((observerHandle, targetHandle), out var visible)
            ? visible
            : throw new InvalidOperationException(
                $"Classic INT actor visibility is not admitted: " +
                $"{observerHandle}->{targetHandle}.");
}

internal sealed class ClassicIntActorHandleTable
{
    private const int NullObjectHandle = 0;
    private readonly Dictionary<object, int> _handles =
        new(ReferenceEqualityComparer.Instance);

    internal int Register(object actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (_handles.TryGetValue(actor, out var handle))
            return handle;
        handle = checked(NullObjectHandle + _handles.Count + 1);
        _handles.Add(actor, handle);
        return handle;
    }

    internal int Require(object actor) =>
        _handles.TryGetValue(actor, out var handle)
            ? handle
            : throw new InvalidOperationException(
                "Classic INT live actor handle is not registered.");
}

internal interface IClassicIntObjectFactory
{
    int Create(ClassicIntObjectCreation source);
}

internal sealed record ClassicIntObjectHandleTable(
    IReadOnlyDictionary<ClassicIntObjectCreation, int> Handles) :
    IClassicIntObjectFactory
{
    public int Create(ClassicIntObjectCreation source) =>
        Handles.TryGetValue(source, out var handle)
            ? handle
            : throw new InvalidOperationException(
                $"Classic INT object creation is not admitted: {source}.");
}

internal interface IClassicIntWorldObjectState
{
    bool ScriptOverrides { get; }
    IReadOnlyDictionary<int, ClassicIntDoorObjectState> Doors { get; }
    int? LightLevel { get; }
    IReadOnlyDictionary<int, ClassicIntCreatedObject> CreatedObjects { get; }
    IReadOnlyList<ClassicIntInventoryEntry> Inventory { get; }
    ClassicIntMapStartOverride? MapStartOverride { get; }
    IReadOnlyList<ClassicIntAttackRequest> AttackRequests { get; }
}

internal sealed record ClassicIntWorldObjectState(
    bool ScriptOverrides,
    IReadOnlyDictionary<int, ClassicIntDoorObjectState> Doors,
    int? LightLevel = null) :
    IClassicIntWorldObjectState
{
    public IReadOnlyDictionary<int, ClassicIntCreatedObject> CreatedObjects
    { get; init; } = new Dictionary<int, ClassicIntCreatedObject>();

    public IReadOnlyList<ClassicIntInventoryEntry> Inventory
    { get; init; } = [];

    public ClassicIntMapStartOverride? MapStartOverride { get; init; }

    public IReadOnlyList<ClassicIntAttackRequest> AttackRequests
    { get; init; } = [];

    internal static ClassicIntWorldObjectState Empty { get; } = new(
        false, new Dictionary<int, ClassicIntDoorObjectState>());

    internal object Save() => new
    {
        ScriptOverrides,
        Doors = Doors.OrderBy(row => row.Key).Select(row => new
        {
            ObjectHandle = row.Key,
            row.Value.Open,
            row.Value.Locked,
        }).ToArray(),
        LightLevel,
        CreatedObjects = CreatedObjects.OrderBy(row => row.Key).Select(row => new
        {
            row.Value.ObjectHandle,
            row.Value.Source.Pid,
            row.Value.Source.Tile,
            row.Value.Source.Elevation,
            row.Value.Source.ScriptId,
        }).ToArray(),
        Inventory = Inventory.OrderBy(row => row.OwnerHandle)
            .ThenBy(row => row.ObjectHandle).ToArray(),
        MapStartOverride,
        AttackRequests,
    };

    internal static ClassicIntWorldObjectState Restore(JsonElement source)
    {
        var attackRequests = source.TryGetProperty("AttackRequests", out var attacks)
            ? attacks.EnumerateArray().Select(row =>
                new ClassicIntAttackRequest(
                    row.GetProperty("ActorHandle").GetInt32(),
                    row.GetProperty("TargetHandle").GetInt32(),
                    row.GetProperty("SourceArguments").EnumerateArray()
                        .Select(value => value.GetInt32()).ToArray())).ToArray()
            : [];
        if (attackRequests.Any(row =>
                row.SourceArguments.Count != ClassicIntProcedureVm.AttackArgumentCount))
            throw new InvalidOperationException(
                "Classic INT saved attack request is invalid.");
        var result = new ClassicIntWorldObjectState(
            source.GetProperty("ScriptOverrides").GetBoolean(),
            source.GetProperty("Doors").EnumerateArray().ToDictionary(
            row => row.GetProperty("ObjectHandle").GetInt32(),
            row => new ClassicIntDoorObjectState(
                row.GetProperty("Open").GetBoolean(),
                row.GetProperty("Locked").GetBoolean())),
            source.TryGetProperty("LightLevel", out var lightLevel) &&
            lightLevel.ValueKind != JsonValueKind.Null
                ? lightLevel.GetInt32()
                : null);
        return result with
        {
            CreatedObjects = source.TryGetProperty("CreatedObjects", out var objects)
                ? objects.EnumerateArray().ToDictionary(
                    row => row.GetProperty("ObjectHandle").GetInt32(),
                    row => new ClassicIntCreatedObject(
                        row.GetProperty("ObjectHandle").GetInt32(),
                        new ClassicIntObjectCreation(
                            row.GetProperty("Pid").GetInt32(),
                            row.GetProperty("Tile").GetInt32(),
                            row.GetProperty("Elevation").GetInt32(),
                            row.GetProperty("ScriptId").GetInt32())))
                : new Dictionary<int, ClassicIntCreatedObject>(),
            Inventory = source.TryGetProperty("Inventory", out var inventory)
                ? inventory.EnumerateArray().Select(row =>
                    new ClassicIntInventoryEntry(
                        row.GetProperty("OwnerHandle").GetInt32(),
                        row.GetProperty("ObjectHandle").GetInt32(),
                        row.GetProperty("Quantity").GetInt32())).ToArray()
                : [],
            MapStartOverride = source.TryGetProperty(
                    "MapStartOverride", out var mapStart) &&
                mapStart.ValueKind != JsonValueKind.Null
                    ? new ClassicIntMapStartOverride(
                        mapStart.GetProperty("TileX").GetInt32(),
                        mapStart.GetProperty("TileY").GetInt32(),
                        mapStart.GetProperty("Elevation").GetInt32(),
                        mapStart.GetProperty("Rotation").GetInt32())
                    : null,
            AttackRequests = attackRequests,
        };
    }
}

internal sealed record ClassicIntProcedureState(
    IReadOnlyDictionary<int, int> ProgramVariables,
    IReadOnlyDictionary<int, int> LocalVariables,
    IReadOnlyDictionary<int, int> ScriptLocalVariables,
    IReadOnlyDictionary<int, int> MapVariables,
    IReadOnlyDictionary<int, int> GlobalVariables,
    IReadOnlyList<int> ValueStack,
    ClassicRetailRandomLifecycleState RandomState)
{
    internal object Save() => this;

    internal static ClassicIntProcedureState Restore(
        JsonElement source,
        ClassicRetailRandomContract randomContract)
    {
        var result = source.Deserialize<ClassicIntProcedureState>() ??
            throw new InvalidOperationException(
                "Classic INT procedure save state is invalid.");
        if (result.ProgramVariables is null || result.LocalVariables is null ||
            result.ScriptLocalVariables is null || result.MapVariables is null ||
            result.GlobalVariables is null || result.ValueStack is null ||
            result.RandomState is null ||
            result.ScriptLocalVariables.Keys.Any(index => index < 0))
            throw new InvalidOperationException(
                "Classic INT procedure save state is invalid.");
        result.RandomState.Validate(randomContract);
        return result;
    }
}

internal sealed record ClassicIntProcedureResult(
    ClassicIntProcedureState State,
    int ExecutedInstructions,
    int ReturnValue,
    IReadOnlyList<ClassicIntMessageEffect> MessageEffects,
    IReadOnlyList<string> SoundEffects,
    ClassicIntWorldObjectState WorldObjects);

internal sealed record ClassicIntMessageEffect(
    int MessageList,
    int MessageId,
    int MessageHandle,
    int? ObjectHandle,
    int? Color);

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
    private const ushort FetchExternal = 0x8014;
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
    private const ushort GreaterThan = 0x8038;
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
    private const ushort CreateObject = 0x80B7;
    private const ushort OverrideMapStart = 0x80A9;
    private const ushort DisplayMessage = 0x80B8;
    private const ushort ScriptOverrides = 0x80B9;
    private const ushort SelfObject = 0x80BC;
    private const ushort SourceObject = 0x80BD;
    private const ushort DudeObject = 0x80BF;
    private const ushort ScriptLocal = 0x80C1;
    private const ushort SetScriptLocal = 0x80C2;
    private const ushort MapVariable = 0x80C3;
    private const ushort SetMapVariable = 0x80C4;
    private const ushort GlobalVariable = 0x80C5;
    private const ushort SetGlobalVariable = 0x80C6;
    private const ushort Attack = 0x80D0;
    private const ushort ObjectCanSeeObject = 0x80DC;
    private const ushort CritterStat = 0x80CA;
    private const ushort Metarule = 0x810B;
    private const ushort MessageString = 0x8105;
    private const ushort AddMultipleToInventory = 0x8116;
    private const ushort GetMonth = 0x8118;
    private const ushort FloatMessage = 0x810A;
    private const ushort PlaySound = 0x80A3;
    private const ushort SetLightLevel = 0x80E9;
    private const ushort GameTime = 0x80EA;
    private const ushort GameTimeHour = 0x80F6;
    private const ushort DoorLock = 0x812E;
    private const ushort DoorUnlock = 0x812F;
    private const ushort DoorIsOpen = 0x8130;
    private const ushort DoorOpen = 0x8131;
    private const ushort DoorClose = 0x8132;
    private const ushort DifficultyLevel = 0x812A;
    private const ushort CombatDifficulty = 0x814F;
    private const ushort SfallArrayLength = 0x8231;
    private const ushort PushInteger = 0xC001;
    private const ushort PushReference = 0x9001;
    internal const int AttackArgumentCount = 7;

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
        return new ClassicIntProgram(
            identity,
            procedures.Values.ToArray(),
            procedures,
            instructions,
            ParseReferences(inventory, "identifierReferences", identity),
            ParseReferences(inventory, "stringReferences", identity));
    }

    internal static ClassicIntProcedureResult Execute(
        ClassicIntProgram program,
        string procedure,
        ClassicIntProcedureState source,
        ClassicIntExpressionContext game,
        IClassicIntWorldObjectState sourceWorldObjects,
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
        var addressStack = new Stack<int>();
        var bases = new Stack<int>();
        var calls = new Stack<(int Offset, ClassicIntProcedure Procedure)>();
        var random = source.RandomState;
        var messageEffects = new List<ClassicIntMessageEffect>();
        var soundEffects = new List<string>();
        var doors = new Dictionary<int, ClassicIntDoorObjectState>(
            sourceWorldObjects.Doors);
        var scriptOverrides = sourceWorldObjects.ScriptOverrides;
        var lightLevel = sourceWorldObjects.LightLevel;
        var createdObjects = new Dictionary<int, ClassicIntCreatedObject>(
            sourceWorldObjects.CreatedObjects);
        var inventory = sourceWorldObjects.Inventory.ToList();
        var mapStartOverride = sourceWorldObjects.MapStartOverride;
        var attackRequests = sourceWorldObjects.AttackRequests.ToList();
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
                case PushReference:
                    stack.Add(instruction.Operand ?? throw Failure(
                        program, procedure, offset, "missing-reference-operand"));
                    break;
                case PushBase:
                    bases.Push(stack.Count);
                    break;
                case PopOpcode:
                    Pop(stack, program, procedure, offset);
                    break;
                case DataToA:
                    addressStack.Push(Pop(stack, program, procedure, offset));
                    break;
                case AToData:
                    if (!addressStack.TryPop(out var addressValue))
                        throw Failure(program, procedure, offset,
                            "address-stack-underflow");
                    stack.Add(addressValue);
                    break;
                case FetchProgram:
                    stack.Add(Read(programVariables, Pop(stack, program, procedure, offset),
                        program, procedure, offset, "program-variable"));
                    break;
                case StoreProgram:
                    Store(programVariables, stack, program, procedure, offset, true);
                    break;
                case FetchExternal:
                    {
                        var reference = Pop(stack, program, procedure, offset);
                        var name = Read(program.IdentifierReferences, reference,
                            program, procedure, offset, "identifier-reference");
                        stack.Add(Read(game.ExternalVariables, name,
                            program, procedure, offset, "external-variable"));
                        break;
                    }
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
                case SourceObject:
                    stack.Add(game.SourceObject ?? throw Failure(
                        program, procedure, offset, "missing-source-object"));
                    break;
                case ObjectCanSeeObject:
                    {
                        var target = Pop(stack, program, procedure, offset);
                        var observer = Pop(stack, program, procedure, offset);
                        var queries = game.ActorQueries ?? throw Failure(
                            program, procedure, offset, "missing-actor-queries");
                        stack.Add(Bool(queries.CanSee(observer, target)));
                        break;
                    }
                case CombatDifficulty:
                    stack.Add(game.CombatDifficulty ?? throw Failure(
                        program, procedure, offset, "missing-combat-difficulty"));
                    break;
                case DifficultyLevel:
                    stack.Add(game.DifficultyLevel ?? throw Failure(
                        program, procedure, offset, "missing-difficulty-level"));
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
                case GreaterThan: Binary(stack, (left, right) => Bool(left > right), program, procedure, offset); break;
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
                case CreateObject:
                    {
                        var scriptId = Pop(stack, program, procedure, offset);
                        var elevation = Pop(stack, program, procedure, offset);
                        var tile = Pop(stack, program, procedure, offset);
                        var pid = Pop(stack, program, procedure, offset);
                        var creation = new ClassicIntObjectCreation(
                            pid, tile, elevation, scriptId);
                        var handle = game.ObjectFactory.Create(creation);
                        if (createdObjects.ContainsKey(handle))
                            throw Failure(program, procedure, offset,
                                "duplicate-created-object-handle");
                        createdObjects.Add(handle,
                            new ClassicIntCreatedObject(handle, creation));
                        stack.Add(handle);
                        break;
                    }
                case ScriptOverrides:
                    scriptOverrides = true;
                    break;
                case PlaySound:
                    {
                        var reference = Pop(stack, program, procedure, offset);
                        soundEffects.Add(Read(program.StringReferences, reference,
                            program, procedure, offset, "string-reference"));
                        break;
                    }
                case GameTime:
                    stack.Add(game.GameTime ?? throw Failure(
                        program, procedure, offset, "missing-game-time"));
                    break;
                case GameTimeHour:
                    stack.Add(game.GameTimeHour ?? throw Failure(
                        program, procedure, offset, "missing-game-time-hour"));
                    break;
                case GetMonth:
                    stack.Add(game.Month ?? throw Failure(
                        program, procedure, offset, "missing-month"));
                    break;
                case SetLightLevel:
                    lightLevel = Pop(stack, program, procedure, offset);
                    break;
                case OverrideMapStart:
                    {
                        var rotation = Pop(stack, program, procedure, offset);
                        var elevation = Pop(stack, program, procedure, offset);
                        var tileY = Pop(stack, program, procedure, offset);
                        var tileX = Pop(stack, program, procedure, offset);
                        mapStartOverride = new ClassicIntMapStartOverride(
                            tileX, tileY, elevation, rotation);
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
                case DoorIsOpen:
                    stack.Add(Bool(Door(Pop(stack, program, procedure, offset)).Open));
                    break;
                case DoorOpen:
                    SetDoor(Pop(stack, program, procedure, offset), open: true);
                    break;
                case DoorClose:
                    SetDoor(Pop(stack, program, procedure, offset), open: false);
                    break;
                case DoorLock:
                    SetDoor(Pop(stack, program, procedure, offset), locked: true);
                    break;
                case DoorUnlock:
                    SetDoor(Pop(stack, program, procedure, offset), locked: false);
                    break;
                case DisplayMessage:
                    {
                        var handle = Pop(stack, program, procedure, offset);
                        messageEffects.Add(MessageEffect(handle, null, null));
                        break;
                    }
                case FloatMessage:
                    {
                        var color = Pop(stack, program, procedure, offset);
                        var handle = Pop(stack, program, procedure, offset);
                        var objectHandle = Pop(stack, program, procedure, offset);
                        messageEffects.Add(MessageEffect(
                            handle, objectHandle, color));
                        break;
                    }
                case AddMultipleToInventory:
                    {
                        var quantity = Pop(stack, program, procedure, offset);
                        var objectHandle = Pop(stack, program, procedure, offset);
                        var ownerHandle = Pop(stack, program, procedure, offset);
                        if (quantity <= 0 || !createdObjects.ContainsKey(objectHandle) ||
                            inventory.Any(row => row.ObjectHandle == objectHandle))
                            throw Failure(program, procedure, offset,
                                "inventory-transfer");
                        inventory.Add(new ClassicIntInventoryEntry(
                            ownerHandle, objectHandle, quantity));
                        break;
                    }
                case Attack:
                    {
                        var arguments = new int[AttackArgumentCount];
                        for (var index = arguments.Length - 1; index >= 0; index--)
                            arguments[index] = Pop(
                                stack, program, procedure, offset);
                        var target = Pop(stack, program, procedure, offset);
                        attackRequests.Add(new ClassicIntAttackRequest(
                            game.SelfObject, target, arguments));
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
                        var procedureIndex = Pop(stack, program, procedure, offset);
                        var argumentCount = Pop(stack, program, procedure, offset);
                        if (procedureIndex < 0 ||
                            procedureIndex >= program.ProcedureOrder.Count)
                            throw Failure(program, procedure, offset, "call-target");
                        var called = program.ProcedureOrder[procedureIndex];
                        if (argumentCount != 0 || called.Instructions.Count == 0)
                            throw Failure(program, procedure, offset, "call-arguments");
                        if (!addressStack.TryPop(out var returnOffset) ||
                            returnOffset != next)
                            throw Failure(program, procedure, offset,
                                "call-return-address");
                        calls.Push((returnOffset, current));
                        current = called;
                        next = called.BodyOffset;
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
            messageEffects,
            soundEffects,
            new ClassicIntWorldObjectState(scriptOverrides, doors, lightLevel)
            {
                CreatedObjects = createdObjects,
                Inventory = inventory,
                MapStartOverride = mapStartOverride,
                AttackRequests = attackRequests,
            });

        ClassicIntDoorObjectState Door(int objectHandle) =>
            doors.TryGetValue(objectHandle, out var door)
                ? door
                : throw Failure(program, procedure, offset, "missing-door-object");

        void SetDoor(int objectHandle, bool? open = null, bool? locked = null)
        {
            var door = Door(objectHandle);
            doors[objectHandle] = door with
            {
                Open = open ?? door.Open,
                Locked = locked ?? door.Locked,
            };
        }

        ClassicIntMessageEffect MessageEffect(
            int handle,
            int? objectHandle,
            int? color)
        {
            var matches = game.MessageHandles
                .Where(row => row.Value == handle).Select(row => row.Key)
                .Take(2).ToArray();
            if (matches.Length != 1)
                throw Failure(program, procedure, offset,
                    "ambiguous-message-handle");
            return new ClassicIntMessageEffect(
                matches[0].MessageList, matches[0].MessageId, handle,
                objectHandle, color);
        }
    }

    private static IReadOnlyDictionary<int, string> ParseReferences(
        JsonElement inventory,
        string property,
        string identity)
    {
        if (!inventory.TryGetProperty(property, out var source))
            return new Dictionary<int, string>();
        var result = new Dictionary<int, string>();
        foreach (var row in source.EnumerateObject())
            if (!int.TryParse(row.Name, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var offset) || string.IsNullOrEmpty(row.Value.GetString()) ||
                !result.TryAdd(offset, row.Value.GetString()!))
                throw new InvalidOperationException(
                    $"Classic INT reference table is invalid: {identity}:{property}.");
        return result;
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

    private static TValue Read<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
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
