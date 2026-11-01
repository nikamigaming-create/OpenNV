using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutScriptPosition
{
    internal static bool Supports(string name) => name == "setpos";
    internal static FalloutScriptFunction? Function(string name, Func<FalloutReferenceWorld> world,
        Func<FalloutFormKey> target) => !Supports(name) ? null : new(
        [FalloutScriptArgumentKind.Identifier, FalloutScriptArgumentKind.Number], arguments =>
        {
            var axis = arguments[0].Identifier?.ToLowerInvariant() switch
            {
                "x" => 0,
                "y" => 1,
                "z" => 2,
                _ => throw new InvalidDataException("SetPos requires an X, Y or Z axis."),
            };
            var position = (float)arguments[1].Number;
            if (!float.IsFinite(position)) throw new InvalidDataException("SetPos requires a finite source coordinate.");
            world().SetPositionAxis(target(), axis, position);
            return 0;
        });
}
