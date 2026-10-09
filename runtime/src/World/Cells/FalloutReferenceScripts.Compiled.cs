using System.Buffers.Binary;
using System.Globalization;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private readonly Dictionary<FalloutFormKey, FalloutCompiledScriptProgram> _compiledDefinitions = [];
    private static void RequireCompiledEffectArguments(FalloutCompiledCommand command, string sourceName)
    {
        var count = command.Arguments.Count;
        var name = sourceName.ToUpperInvariant();
        if (name is "ADDPERK" or "REMOVEPERK" && count != 1)
            throw new NotSupportedException("Compiled perk alternate-owner arguments have no shared owner.");
        if (name == "SETCOMBATSTYLE" && count != 1)
            throw new NotSupportedException("Compiled default combat-style reset has no shared owner.");
        if (name is "APPLYIMAGESPACEMODIFIER" or "REMOVEIMAGESPACEMODIFIER" && count != 1)
            throw new NotSupportedException("Compiled numeric image-space strength has no shared compositor owner.");
        if (name == "SAY" && count > 2 || name == "SAYTO" && count > 3)
            throw new NotSupportedException("Compiled extended speech arguments have no shared speech owner.");
        if (name == "FORCEWEATHER" && count != 1)
            throw new NotSupportedException("Compiled weather-override flags have no shared sky owner.");
    }

    private FalloutCompiledScriptProgram CompiledObjectProgram(FalloutReferenceInstance instance)
    {
        var source = instance.Script?.Record ?? throw new InvalidDataException("Compiled object invocation has no SCPT.");
        if (!_compiledDefinitions.TryGetValue(source.FormKey, out var program))
        {
            program = FalloutCompiledScriptProgram.Read(source, FalloutScriptScope.Standalone(source), standalone: true);
            foreach (var block in program.Events) _ = FalloutCompiledScriptEvents.Name(block.Event);
            _compiledDefinitions.Add(source.FormKey, program);
        }
        return program;
    }

    private IReadOnlyList<FalloutReferenceScriptEventResult> DispatchCompiledFrame(
        FalloutReferenceInstance instance, IReadOnlyList<FalloutReferenceScriptEvent> events,
        IReadOnlyDictionary<string, FalloutReferenceScriptEvent> admitted, double elapsedSeconds,
        Action? observeActivationBegin, Action? observeActivationEnd)
    {
        if (!instance.DeletePending && !instance.Deleted && admitted.TryGetValue("OnActivate", out var defaultActivation) &&
            HasIndependentDefaultActivation(instance))
            return DispatchIndependentDefaultActivation(instance, events, defaultActivation, elapsedSeconds,
                observeActivationBegin, observeActivationEnd);
        if (instance.ScriptStoppedFrame is not null || instance.CompletedScriptContinuation is not null)
            return events.Select(item => new FalloutReferenceScriptEventResult(instance.Reference, item.Name, 0,
                "Compiled: Source statement cursor cannot authorize compiled event offsets or replay its prefix.")).ToArray();
        var counts = admitted.Keys.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var failure = instance.ScriptError;
        if (failure is null && !instance.DeletePending && !instance.Deleted && events.Count != 0)
        {
            var running = "Compiled";
            try
            {
                var program = CompiledObjectProgram(instance);
                foreach (var block in program.Events)
                {
                    var name = FalloutCompiledScriptEvents.Name(block.Event);
                    if (!admitted.ContainsKey(name)) continue;
                    running = "Compiled/" + name;
                    FalloutCompiledScriptEvents.RequireAdmission(program, block);
                    if (block.Event == 2) observeActivationBegin?.Invoke();
                    foreach (var _ in CompiledSteps(instance.Reference, program, block, elapsedSeconds, block.Event == 2 ? admitted[name].ActionReference : null)) { }
                    if (block.Event == 2) observeActivationEnd?.Invoke();
                    ++counts[name];
                }
            }
            catch (Exception error)
            {
                failure = running + ": " + error.Message;
                // A closed compiled failure retains its exact source hash and
                // locals in the existing world snapshot. It cannot be resumed
                // through a source statement cursor or SCTX recovery routine.
                instance.ScriptError = failure;
            }
        }
        return events.Select(item => new FalloutReferenceScriptEventResult(instance.Reference, item.Name,
            counts[FalloutReferencePackageEvents.CanonicalName(item.Name)], failure)).ToArray();
    }

    // Binary instructions select the same shared state/effect delegates as
    // ordinary source execution. Internal form aliases are bound from decoded
    // operands; no executable source statement or EDID is reconstructed.
    internal IEnumerable<bool> CompiledSteps(FalloutFormKey caller, FalloutCompiledScriptProgram program,
        FalloutCompiledEvent? block = null, double seconds = 0, FalloutFormKey? action = null,
        Action<FalloutScriptManualSaveRequests.Entered>? observeInvocation = null,
        FalloutCompiledExecutionCursor? cursor = null, Func<bool>? canContinue = null,
        string? executionScope = null, IFalloutCompiledEventLocalAuthority? localAuthority = null)
    {
        var winning = records.GetEffective(program.Source.FormKey);
        if (winning.Plugin != program.Source.Plugin || winning.HeaderOffset != program.Source.HeaderOffset)
            throw new InvalidDataException("Compiled invocation differs from its winning source owner.");
        if (!double.IsFinite(seconds) || seconds < 0 || program.CompiledFlag != 1)
            throw new NotSupportedException("Compiled invocation time/flag is outside its owned domain.");
        var sourceKind = FalloutScriptSourceKinds.Classify(program.ScriptType);
        if (localAuthority is not null)
        {
            if (!program.Standalone || block is null || cursor is null || localAuthority.Locals.Script != program.Source.FormKey)
                throw new InvalidDataException("Compiled independent event has no genuine Script/event-list/cursor owner.");
            localAuthority.Require(records, caller, program, block, cursor, seconds, action);
            var ordinal = program.Events.Select((row, index) => (row, index)).Single(pair => ReferenceEquals(pair.row, block)).index;
            if (executionScope is not null && executionScope != FalloutCompiledSliceReceipt.EventScope(program, ordinal))
                throw new InvalidDataException("Compiled independent event scope differs from its actual SCDA ordinal.");
        }
        else if (sourceKind == FalloutScriptSourceKind.MagicEffect)
            throw new NotSupportedException("Compiled magic effect requires its actual instance/event-list local owner.");
        if (program.Standalone && localAuthority is null)
        {
            var owner = records.GetEffective(caller);
            var matchingOwner = program.ScriptType == 0 && owner.Signature is "REFR" or "ACHR" or "ACRE" ||
                program.ScriptType == 1 && owner.Signature == "QUST";
            if (!matchingOwner || LocalScript(caller).FormKey != program.Source.FormKey)
                throw new NotSupportedException("Compiled event differs from its attached calling script.");
        }
        else if (!program.Standalone && program.LocalCount != 0)
            throw new NotSupportedException("Embedded compiled local/event-list continuation has no owner.");
        var flow = FalloutCompiledControlFlow.Read(block is null ? program.ResultInstructions() : program.EventInstructions(block), block?.End ?? 0);
        var budget = new FalloutScriptExecutionBudget(100_000 - (cursor?.State.BudgetSpent ?? 0));
        var current = -1;
        var bindings = FalloutScriptBindings.ForCompiled(records, localAuthority is null ? CompiledBindingOwner(caller, program) : program.Source, program.Source,
            target => LocalScript(target.FormKey));
        Func<string, FalloutScriptFunction?>? sharedFunction = null;
        Action<string, IReadOnlyList<string>>? sharedCommand = null;
        var sourceSeconds = (double?)world.CompiledScriptSeconds(seconds) ?? seconds;
        _ = Steps(caller, bindings, null, action, sourceSeconds, budget: budget,
            bindCompiledOwners: (function, command) => { sharedFunction = function; sharedCommand = command; },
            compiledStatement: () => current, effectLocals: localAuthority?.Locals);
        FalloutCompiledOperandContext operands = null!;
        operands = new FalloutCompiledOperandContext(ReferenceValue, ReadVariable, ReadGlobal, Query, budget,
            opcode => world.NativePlugins?.Declaration(opcode) ?? FalloutCompiledCommandDeclarations.Get(opcode, records),
            NativeQuery);
        var saveProgram = FalloutScriptSaveProgram.Capture(program, block);
        var saveScope = executionScope ?? saveProgram.EventScopeSha256 ?? program.Scope.ScopeSha256;
        return ExecuteSourceCall();

        IEnumerable<bool> ExecuteSourceCall()
        {
            using var context = world.EnterCompiledScriptContext(caller, program, seconds, action, localAuthority);
            var steps = cursor is null
                ? world.ScriptManualSaves.ExecuteCompiled(caller, program.Source, program.ProgramSha256,
                    ExecuteInstructions(), saveScope, Observe, saveProgram)
                : world.ScriptManualSaves.ExecuteCompiledSlice(caller, program.Source, program.ProgramSha256,
                    ExecuteInstructions(), saveScope, cursor, canContinue ?? (() => true), Observe, saveProgram);
            IEnumerator<bool> enumerator;
            try { enumerator = steps.GetEnumerator(); }
            catch (Exception failure) { context?.Fail(failure); throw; }
            try
            {
                while (true)
                {
                    bool moved;
                    try { moved = enumerator.MoveNext(); }
                    catch (Exception failure) { context?.Fail(failure); throw; }
                    if (!moved) { context?.Returned(); yield break; }
                    yield return enumerator.Current;
                }
            }
            finally
            {
                try { enumerator.Dispose(); }
                catch (Exception failure) { context?.Fail(failure); throw; }
            }
            void Observe(FalloutScriptManualSaveRequests.Entered actual)
            { context?.Observe(actual); observeInvocation?.Invoke(actual); }
        }

        IEnumerable<bool> ExecuteInstructions()
        {
            var execution = cursor is null ? flow.Execute(Evaluate, Apply, budget,
                instruction => { current = instruction.Offset; world.ScriptManualSaves.ReachedCompiledInstruction(instruction.Offset); }) : flow.ExecuteResumable(Evaluate, Apply, budget,
                cursor, instruction => { current = instruction.Offset; world.ScriptManualSaves.ReachedCompiledInstruction(instruction.Offset); });
            using var instructions = execution.GetEnumerator();
            while (true)
            {
                bool moved;
                try { moved = instructions.MoveNext(); }
                catch (Exception error)
                {
                    if (error.Message.StartsWith($"Compiled {program.Source.FormKey} at SCDA offset ", StringComparison.Ordinal))
                        throw;
                    throw Failure(error);
                }
                if (!moved) yield break;
                yield return instructions.Current;
            }
        }

        Exception Failure(Exception reason) => new NotSupportedException(
            $"Compiled {program.Source.FormKey} at SCDA offset {current:x}: {reason.Message}", reason);
        FalloutScriptValue Evaluate(ReadOnlyMemory<byte> expression)
        {
            try { return FalloutCompiledOperands.Expression(expression, operands); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException or OverflowException)
            { throw Failure(error); }
        }
        FalloutPluginRecord LocalScript(FalloutFormKey target)
        {
            var record = records.GetEffective(target);
            if (record.Signature == "QUST") return FalloutScriptLocals.AttachedScript(records, record) ??
                throw new InvalidDataException("Compiled quest variable has no attached script.");
            var instance = world.Get(target);
            if (record.Signature is "ACHR" or "ACRE")
            {
                var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(instance.Base), 512, instance.Templates);
                if (FalloutScriptLocals.AttachedScript(records, owner)?.FormKey != instance.Script?.Record.FormKey)
                    throw new NotSupportedException("Compiled actor variable requires its bound template-script owner.");
            }
            return instance.Script?.Record ?? throw new InvalidDataException("Compiled reference has no attached script.");
        }
        bool ReferenceSlot(FalloutPluginRecord script, uint slot) => script.ReadSubrecords().Any(field =>
            field.Signature == "SCRV" && field.Data.Length == 4 && BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) == slot);
        FalloutFormKey VariableOwner(FalloutCompiledVariable variable)
        {
            if (variable.OwnerReference is { } reference) return NonNullReference(reference);
            if (!program.Standalone)
                throw new NotSupportedException("Implicit embedded local/event-list operand has no owner.");
            return caller;
        }
        FalloutScriptValue ReadVariable(FalloutCompiledVariable variable)
        {
            if (localAuthority is not null && variable.OwnerReference is null) return localAuthority.Read(variable);
            var target = VariableOwner(variable); var script = LocalScript(target);
            var reference = ReferenceSlot(script, variable.Slot) &&
                (!FalloutScriptLocals.HasMixedStorage(script, variable.Slot) || variable.Storage == (byte)'f');
            RequireCompiledSlot(script, variable.Slot, variable.Storage, reference);
            var raw = world.ReadVariable(quests, target, variable.Slot);
            FalloutScriptLocals.RequireCompiledValue(script, variable.Slot, raw);
            return reference ? FalloutScriptValue.Form(FalloutScriptLocals.HasMixedStorage(script, variable.Slot)
                ? (uint)BitConverter.DoubleToUInt64Bits(raw) : raw) : (FalloutScriptValue)raw;
        }
        void AssignVariable(FalloutCompiledVariable variable, FalloutScriptValue value)
        {
            if (localAuthority is not null && variable.OwnerReference is null) { localAuthority.Write(variable, value); return; }
            var target = VariableOwner(variable); var script = LocalScript(target);
            var reference = ReferenceSlot(script, variable.Slot) &&
                (!FalloutScriptLocals.HasMixedStorage(script, variable.Slot) || variable.Storage == (byte)'f');
            RequireCompiledSlot(script, variable.Slot, variable.Storage, reference);
            if (reference)
            {
                if (value.Kind == FalloutScriptValueKind.Number && value.Number == 0) value = FalloutScriptValue.Form(0);
                if (value.Kind != FalloutScriptValueKind.Form)
                    throw new NotSupportedException("Compiled reference assignment requires a typed form or literal null.");
            }
            else if (value.Kind != FalloutScriptValueKind.Number)
                throw new NotSupportedException("Compiled numeric assignment has an unowned string/array/reference storage effect.");
            if (variable.Storage is (byte)'s' or (byte)'l' && (value.Number != Math.Truncate(value.Number) ||
                value.Number < int.MinValue || value.Number > int.MaxValue))
                throw new NotSupportedException("Compiled integer assignment outside exact Int32 has no storage contract.");
            var stored = reference && FalloutScriptLocals.HasMixedStorage(script, variable.Slot)
                ? FalloutScriptLocals.EncodeSharedReference(value.Number) : value.Number;
            FalloutScriptLocals.RequireCompiledValue(script, variable.Slot, stored);
            world.WriteVariable(quests, target, variable.Slot, stored);
        }
        FalloutScriptValue ReferenceValue(ushort index)
        {
            var declared = program.Reference(index);
            if (declared.Form is { } form) return FalloutScriptValue.Form(records.RuntimeFormId(form));
            var slot = checked((ushort)(declared.Variable ?? throw new InvalidDataException("Compiled reference table has no binding.")));
            return ReadVariable(new(null, slot, (byte)'f'));
        }
        FalloutFormKey NonNullReference(ushort index)
        {
            var value = ReferenceValue(index);
            if (value.Kind != FalloutScriptValueKind.Form || value.Number == 0)
                throw new InvalidDataException("Compiled reference receiver/owner is null or untyped.");
            return value.FormKey(records);
        }
        FalloutScriptValue ReadGlobal(ushort index)
        {
            var key = NonNullReference(index);
            if (records.GetEffective(key).Signature != "GLOB") throw new InvalidDataException("Compiled global table entry is not GLOB.");
            return (host.Globals ?? throw new NotSupportedException("Compiled global has no shared state owner.")).Get(key);
        }
        string Token(FalloutCompiledArgument argument)
        {
            var value = argument.Value;
            if (value.Kind == FalloutScriptValueKind.Form)
                return value.Number == 0 ? "0" : bindings.BindCompiledForm(value.FormKey(records));
            if (value.Kind == FalloutScriptValueKind.String)
                return !value.Text.Contains('"') ? "\"" + value.Text + "\"" :
                    throw new NotSupportedException("Compiled string cannot bind to the shared literal command surface.");
            if (value.Kind != FalloutScriptValueKind.Number)
                throw new NotSupportedException("Compiled command array value has no adapter owner.");
            return FalloutCompiledParameterNames.Read(argument.SourceParameterType, value.Number) ?? value.Number.ToString("R", CultureInfo.InvariantCulture);
        }
        string Name(FalloutCompiledCommand command)
        {
            var declaration = FalloutCompiledCommandDeclarations.Get(command.Opcode, records);
            var receiver = command.Receiver is { } reference ? NonNullReference(reference) : (FalloutFormKey?)null;
            if (receiver is { } target && records.RuntimeFormId(target) != 0x14 &&
                records.GetEffective(target).Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("Compiled command receiver is not a placed reference/engine player.");
            if (receiver is null && declaration.RequiresReference && records.RuntimeFormId(caller) != 0x14 &&
                records.GetEffective(caller).Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("Compiled reference command has no actual calling reference.");
            return receiver is { } key ? bindings.BindCompiledForm(key) + "." + declaration.Name : declaration.Name;
        }
        FalloutScriptValue Invoke(FalloutCompiledCommand command, FalloutScriptFunction function)
        {
            var arguments = command.Arguments.Select((argument, index) =>
            {
                if (index >= function.Arguments.Count)
                    throw new NotSupportedException("Compiled command variadic argument conversion is unowned.");
                var kind = function.Arguments[index];
                var identifier = kind is FalloutScriptArgumentKind.Identifier or FalloutScriptArgumentKind.OptionalIdentifier
                    ? Token(argument) : null;
                var value = argument.Value;
                if (kind is FalloutScriptArgumentKind.String or FalloutScriptArgumentKind.OptionalString or FalloutScriptArgumentKind.SourceString)
                    if (value.Kind != FalloutScriptValueKind.String)
                        throw new InvalidDataException("Compiled query string kind differs from its shared owner.");
                return new FalloutScriptArgument(value, identifier);
            }).ToArray();
            return function.InvokeValue(arguments);
        }
        FalloutScriptValue Query(FalloutCompiledCommand command)
        {
            var name = Name(command);
            if (!FalloutCompiledSemanticOwners.IsQuery(FalloutCompiledCommandDeclarations.Get(command.Opcode, records).Name))
                throw new NotSupportedException($"Compiled query {command.Opcode:x4}/{name} has no admitted authoritative execution owner.");
            var function = sharedFunction!(name) ?? throw new NotSupportedException(
                $"Compiled query {command.Opcode:x4}/{name} has no authoritative shared gameplay owner.");
            return Invoke(command, function);
        }
        FalloutScriptValue? NativeQuery(ushort opcode, ushort? receiver, ReadOnlyMemory<byte> payload)
        {
            if (world.NativePlugins?.Declaration(opcode) is null) return null;
            if (localAuthority is not null)
                throw new NotSupportedException("Original native independent-event call requires its genuine event-list/object projection.");
            var call = FalloutNativePluginCompiledCalls.Bind(caller, program, opcode, receiver, payload,
                () => FalloutCompiledOperands.Command(opcode, receiver, payload, operands).Arguments
                    .Select(argument => argument.Value).ToArray());
            return world.NativePlugins.Invoke(call);
        }
        void Apply(FalloutCompiledInstruction instruction)
        {
            try
            {
                if (instruction.Opcode == 0x15)
                {
                    if (instruction.Receiver is not null) throw new InvalidDataException("Compiled Set has a command receiver prefix.");
                    var cursor = new FalloutCompiledOperandCursor(instruction.Payload);
                    if (cursor.Peek == 'G')
                    {
                        _ = cursor.Byte(); var target = NonNullReference(cursor.UInt16());
                        if (records.GetEffective(target).Signature != "GLOB") throw new InvalidDataException("Compiled Set global is not GLOB.");
                        var value = Evaluate(cursor.Bytes(cursor.UInt16())); cursor.RequireEnd();
                        if (value.Kind != FalloutScriptValueKind.Number || !float.IsFinite((float)value.Number))
                            throw new InvalidDataException("Compiled Set global exceeds numeric Float32 storage.");
                        (host.Globals ?? throw new NotSupportedException("Compiled global has no shared state owner.")).Set(target, (float)value.Number);
                    }
                    else
                    {
                        var variable = cursor.Variable(allowLong: true);
                        var value = Evaluate(cursor.Bytes(cursor.UInt16())); cursor.RequireEnd(); AssignVariable(variable, value);
                    }
                    return;
                }
                if (NativeQuery(instruction.Opcode, instruction.Receiver, instruction.Payload) is not null) return;
                var declaration = FalloutCompiledCommandDeclarations.Get(instruction.Opcode, records);
                var query = FalloutCompiledSemanticOwners.IsQuery(declaration.Name);
                if (!query && !FalloutCompiledSemanticOwners.IsEffect(declaration.Name))
                    throw new NotSupportedException($"Compiled command {instruction.Opcode:x4} has no admitted authoritative execution owner.");
                var command = FalloutCompiledOperands.Command(instruction.Opcode, instruction.Receiver, instruction.Payload, operands);
                var name = Name(command);
                if (FalloutCompiledSemanticOwners.IsMessage(declaration.Name))
                {
                    var message = command.Message ?? throw new InvalidDataException("Compiled message has no decoded special arguments.");
                    var form = command.Arguments.Single().Value;
                    if (form.Kind != FalloutScriptValueKind.Form || form.Number == 0)
                        throw new InvalidDataException("Compiled message operand is not a nonnull form.");
                    var target = records.RuntimeFormKey(checked((uint)form.Number));
                    if (records.GetEffective(target).Signature != "MESG")
                        throw new InvalidDataException("Compiled ShowMessage target is not MESG.");
                    var calling = command.Receiver is { } receiver ? NonNullReference(receiver) : caller;
                    host.Apply(new(FalloutReferenceEffectKind.Message, caller, target,
                        Message: new(program.Source.FormKey, calling, message.Substitutions)));
                }
                else if (query) _ = Query(command);
                else
                {
                    RequireCompiledEffectArguments(command, declaration.Name);
                    sharedCommand!(name, command.Arguments.Select(Token).ToArray());
                }
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException or OverflowException)
            { throw Failure(error); }
        }
    }

    private static void RequireCompiledSlot(FalloutPluginRecord script, uint index, byte type, bool requireReference)
    {
        if (script.Signature != "SCPT" || index == 0) throw new InvalidDataException("Compiled variable has no SCPT slot.");
        _ = FalloutScriptLocals.ReadDeclarations(script, FalloutScriptDeclarationAuthority.CompiledVanilla);
        var fields = script.ReadSubrecords().ToArray();
        var declarations = fields.Where(field => field.Signature == "SLSD" && field.Data.Length == 24 &&
            BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) == index).ToArray();
        var flag = type is (byte)'s' or (byte)'l' ? 1 : 0;
        if (declarations.Length == 0 || !declarations.Any(field => field.Data.Span[16] == flag))
            throw new InvalidDataException("Compiled variable type/index differs from its SLSD declaration.");
        var reference = fields.Any(field => field.Signature == "SCRV" && field.Data.Length == 4 &&
            BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) == index);
        if (reference != requireReference && !FalloutScriptLocals.HasMixedStorage(script, index))
            throw new InvalidDataException("Compiled variable class differs from its source table.");
    }
}
