using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    // Original vector housing. No retail texture, raster cache, animation or input interception.
    private sealed partial class PipBoyHousing : Control
    {
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Resized += QueueRedraw;
        }

        public override void _Draw()
        {
            var bounds = new Rect2(Vector2.One * 8, Size - Vector2.One * 16);
            var shell = Box(new Color("444b3c"), new Color("879079"), border: 3);
            shell.CornerRadiusTopLeft = shell.CornerRadiusBottomRight = 36;
            shell.CornerRadiusTopRight = shell.CornerRadiusBottomLeft = 18;
            shell.ShadowColor = new Color("030603");
            shell.ShadowSize = 8;
            DrawStyleBox(shell, bounds);
            shell.Dispose();
            var inset = Box(new Color("080f09"), new Color("20281e"), border: 5);
            DrawStyleBox(inset, new Rect2(26, 100, Size.X - 52, Size.Y - 152));
            inset.Dispose();
            // Split screws, recessed seams and a short speaker grille belong to the casing.
            foreach (var point in new[] { new Vector2(20, 25), new Vector2(Size.X - 20, 25),
                new Vector2(20, Size.Y - 25), new Vector2(Size.X - 20, Size.Y - 25) })
            {
                DrawCircle(point + Vector2.Down, 7, new Color("171d16"));
                DrawCircle(point, 5, new Color("9aa08a"));
                DrawLine(point + new Vector2(-3, 2), point + new Vector2(3, -2), new Color("333a2d"), 2, true);
            }
            for (var i = 0; i < 9; i++)
            {
                var x = Size.X * .52f + i * 6;
                DrawLine(new Vector2(x, 40), new Vector2(x, 69), new Color("242d22"), 3, true);
            }
            DrawLine(new Vector2(38, 91), new Vector2(Size.X - 38, 91), new Color("242c21"), 2);
            DrawCircle(new Vector2(Size.X - 32, 82), 4, new Color("b5f58b"));
        }
    }

    private sealed partial class PhosphorReticle : Control
    {
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Resized += QueueRedraw;
        }

        public override void _Draw()
        {
            var center = Size / 2;
            var radius = Mathf.Min(Size.X, Size.Y) * .34f;
            var dim = new Color("29452b");
            var bright = new Color("5c8e4b");
            for (var i = 1; i <= 3; i++)
                DrawArc(center, radius * i / 3, 0, Mathf.Tau, 64, dim, 1, true);
            DrawLine(center - Vector2.Right * radius * 1.2f, center + Vector2.Right * radius * 1.2f, dim, 1);
            DrawLine(center - Vector2.Down * radius * 1.2f, center + Vector2.Down * radius * 1.2f, dim, 1);
            // A static instrument motif, deliberately not a gameplay map or invented status.
            DrawArc(center, radius, -.7f, .2f, 24, bright, 3, true);
            DrawCircle(center, 4, bright);
            DrawLine(center, center + new Vector2(.64f, -.76f) * radius, bright, 2, true);
        }
    }
}
