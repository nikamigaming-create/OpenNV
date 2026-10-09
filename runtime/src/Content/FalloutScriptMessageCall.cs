namespace OpenNV.Runtime.Content;

// The actual compiled dispatch supplies the program and caller. A message
// cannot recover that ownership from a diagnostic name or invent a result.
internal sealed record FalloutScriptMessageCall(FalloutFormKey Program, FalloutFormKey Caller,
    IReadOnlyList<double> Substitutions)
{
    internal void Validate()
    {
        if (Program.ObjectId == 0 || Caller.ObjectId == 0 ||
            string.IsNullOrWhiteSpace(Program.OwnerPlugin) || string.IsNullOrWhiteSpace(Caller.OwnerPlugin) ||
            Substitutions is null || Substitutions.Count > 9 || Substitutions.Any(value => !double.IsFinite(value)))
            throw new InvalidDataException("Compiled message has invalid program/caller/numeric ownership.");
    }
}
