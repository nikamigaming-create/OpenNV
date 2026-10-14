using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPackageCommandFields
{
    internal void AttachScript(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey? script) =>
        Execute(invocation, "script-Extra-setter", scope =>
        {
            RequireLane(FalloutPackageCommandFieldLane.ScriptExtra); ValidateForm(script);
            if (_values.ScriptExtra is null)
            {
                // The constructor owns a real C# Extra object first. Current
                // list membership publishes only after its fields are written.
                var identity = Guid.NewGuid();
                Store(scope, FalloutPackageCommandField.ScriptConstruction, @object: identity);
                Store(scope, FalloutPackageCommandField.ScriptForm, form: script);
                Store(scope, FalloutPackageCommandField.ScriptConstructorEventList);
                Store(scope, FalloutPackageCommandField.ScriptPresence, bits: 1, @object: identity);
            }
            else Store(scope, FalloutPackageCommandField.ScriptForm, form: script);
            return true;
        });

    internal bool PublishScriptEventList(FalloutPackageCommandFieldInvocation invocation, Guid? eventList,
        Guid? actualEventFactory, IReadOnlyList<FalloutPackageScriptEventRow?>? rows = null) =>
        Execute(invocation, "script-event-list-setter", scope =>
        {
            RequireLane(FalloutPackageCommandFieldLane.ScriptExtra);
            if (_values.ScriptExtra is null) return false;
            if (eventList == Guid.Empty || actualEventFactory == Guid.Empty || (eventList is null) != (actualEventFactory is null) ||
                eventList is null && rows is { Count: > 0 } || eventList is not null && rows is null)
                throw new InvalidDataException("Script event-list publication lost its actual constructor/membership factory.");
            ValidateRows(rows ?? []);
            scope.PublishedRows = rows is null ? [] : Array.AsReadOnly(rows.ToArray()); scope.EventFactory = actualEventFactory;
            Store(scope, FalloutPackageCommandField.ScriptEventList, @object: eventList); return true;
        });

    internal FalloutPackageCommandEventMark MarkPackageEvent(FalloutPackageCommandFieldInvocation invocation,
        FalloutFormKey package, uint mask)
    {
        Execute(invocation, "package-event-marker", scope =>
        {
            ValidateForm(package); RequireLane(FalloutPackageCommandFieldLane.ScriptExtra);
            var extra = _values.ScriptExtra;
            if (extra?.EventList is null || extra.Rows.Count == 0)
            { scope.EventMark = scope.EventMark! with { Result = false }; return false; }
            // The original list has nullable data pointers. A null data row
            // terminates each walk; it differs from a wildcard row's null form.
            Guid? selected = null, empty = null;
            foreach (var row in extra.Rows)
            {
                if (row is null) break;
                if (row.Form == package) { selected = row.Identity; break; }
                if (row.Form is null) empty = row.Identity;
            }
            selected ??= empty;
            var result = selected is not null;
            if (selected is { } matching) Mark(matching);
            foreach (var row in extra.Rows)
            {
                if (row is null) break;
                if (row.Form is not null) continue;
                Mark(row.Identity); result = true; break;
            }
            scope.EventMark = scope.EventMark! with { Result = result }; return result;
            void Mark(Guid row)
            {
                var current = _values.ScriptExtra!.Rows.Single(value => value?.Identity == row)!;
                Store(scope, FalloutPackageCommandField.EventMask, bits: current.Mask | mask, row: row);
            }
        }, scope => scope.EventMark = new(invocation, package, mask, scope.Entered, scope.Entered, null, [], false, null));
        return LastEventMark!;
    }
}
