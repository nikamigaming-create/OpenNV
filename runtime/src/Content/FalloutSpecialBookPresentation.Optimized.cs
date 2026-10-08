namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    // Optimized Win32 declarations keep Float32 values in SSE registers and
    // immediate frame slots. Admit their consumer relationships, including the
    // native transform fields and paired factories, rather than counting loads.
    private static (float[] Pose, float[] Colors, float[] Radius, float[] Slope, float[] Factors, int Budget)
        ReadOptimizedSpecialBook(byte[] code, Func<uint, int, byte[]> read, int model, int initial, int firstButton,
            int cameraStart, int camera, IReadOnlyList<int> menus)
    {
        float Number(uint address) => BitConverter.ToSingle(read(address, sizeof(float)));
        float Immediate(int at) => BitConverter.Int32BitsToSingle(unchecked((int)U32(code, at)));
        void Require(bool valid, string lane)
        {
            if (!valid) throw new NotSupportedException($"Owned SPECIAL book optimized {lane} declaration is unbound.");
        }
        int One(int start, int end, string lane, params int[] pattern)
        {
            var matches = SpecialBookMatches(code, start, end, pattern);
            Require(matches.Count == 1, lane);
            return matches[0];
        }

        var pose = One(model, initial, "pose",
            0xf3, 0x0f, 0x10, 0x05, -1, -1, -1, -1, // scalar scale
            0x83, 0xc4, 4, 0x0f, 0x54, 0x05, -1, -1, -1, -1, // magnitude mask
            0xc7, 0x45, -1, -1, -1, -1, -1, 0x8b, 0x40, -1, // depth and model receiver
            0xc7, 0x45, -1, -1, -1, -1, -1, 0xf3, 0x0f, 0x11, 0x40, 0x64,
            0x0f, 0x57, 0xc0, 0xa1, -1, -1, -1, -1, 0x0f, 0x14, 0xc6,
            0x8b, 0x48, -1, 0x8b, 0x45, -1, 0x66, 0x0f, 0xd6, 0x41, 0x58, 0x89, 0x41, 0x60,
            0xa1, -1, -1, -1, -1, 0x8b, 0x40, -1, 0xd9, 0x45, -1, 0xd9, 0xfb,
            0xd9, 0x5d, -1, 0xd9, 0x5d, -1);
        var owner = U32(code, pose + 44);
        var modelField = code[pose + 27];
        var depthSlot = code[pose + 20];
        var angleSlot = code[pose + 30];
        Require(code[pose + 53] == modelField && code[pose + 72] == modelField &&
            code[pose + 56] == depthSlot && code[pose + 75] == angleSlot &&
            U32(code, pose + 66) == owner && depthSlot != angleSlot, "pose frame flow");
        var loader = One(model, pose, "model receiver",
            0x8b, 0x0d, -1, -1, -1, -1, 0x89, 0x41, -1,
            0xa1, -1, -1, -1, -1, 0xff, 0x70, -1, 0xe8, -1, -1, -1, -1,
            0xa1, -1, -1, -1, -1, 0x0f, 0x57, 0xf6);
        Require(loader + 30 == pose && U32(code, loader + 2) == owner &&
            U32(code, loader + 10) == owner && U32(code, loader + 23) == owner &&
            code[loader + 8] == modelField && code[loader + 16] == modelField, "model receiver flow");
        Require(read(U32(code, pose + 14), 16).Chunk(4).All(bytes => U32(bytes, 0) == 0x7fffffff), "scale mask");

        var matrix = One(pose + 84, initial, "axis rotation",
            0xf3, 0x0f, 0x10, 0x15, -1, -1, -1, -1,
            0xf3, 0x0f, 0x10, 0x5d, -1, 0xf3, 0x0f, 0x10, 0x65, -1,
            0xf3, 0x0f, 0x5c, 0xd3, 0x0f, 0x28, 0xc3, 0x0f, 0x28, 0xcc,
            0xf3, 0x0f, 0x59, 0xce, 0xf3, 0x0f, 0x58, 0xc2, 0x0f, 0x28, 0xea,
            0xf3, 0x0f, 0x59, 0xee, 0xf3, 0x0f, 0x11, 0x40, 0x34,
            0x0f, 0x28, 0xd5, 0x0f, 0x28, 0xc5, 0xf3, 0x0f, 0x58, 0xd1,
            0xf3, 0x0f, 0x5c, 0xc1, 0x0f, 0x28, 0xcd, 0xf3, 0x0f, 0x58, 0xcb,
            0xf3, 0x0f, 0x11, 0x50, 0x38, 0xf3, 0x0f, 0x11, 0x40, 0x3c,
            0xf3, 0x0f, 0x11, 0x40, 0x40, 0x0f, 0x28, 0xc4, 0xf3, 0x0f, 0x58, 0xc5,
            0xf3, 0x0f, 0x11, 0x48, 0x44, 0xf3, 0x0f, 0x5c, 0xec,
            0xf3, 0x0f, 0x11, 0x50, 0x4c, 0xf3, 0x0f, 0x11, 0x48, 0x54,
            0xf3, 0x0f, 0x11, 0x40, 0x48, 0xf3, 0x0f, 0x11, 0x68, 0x50);
        Require(matrix == pose + 84 && code[matrix + 12] == code[pose + 80] &&
            code[matrix + 17] == code[pose + 83] && Number(U32(code, matrix + 4)) == 1, "axis rotation flow");

        var radius = One(initial, firstButton, "model-bound light radius",
            0xf3, 0x0f, 0x10, 0x42, 0x0c, 0xf3, 0x0f, 0x59, 0x05, -1, -1, -1, -1,
            0x8b, 0x40, 0x0c, 0x0f, 0x29, 0x45, -1, 0xf3, 0x0f, 0x11, 0x04, 0x24, 0xff, 0xd0);
        var light = One(radius, firstButton, "light colors",
            0xf3, 0x0f, 0x10, 0x05, -1, -1, -1, -1, 0x0f, 0x14, 0xc0,
            0x66, 0x0f, 0xd6, 0x86, 0xd4, 0, 0, 0, 0x0f, 0x28, 0x45, -1,
            0x89, 0x46, 0x60, 0xc7, 0x45, -1, -1, -1, -1, -1, 0x8b, 0x45, -1,
            0x89, 0x86, 0xdc, 0, 0, 0, 0x0f, 0x14, 0xc1, 0x66, 0x0f, 0xd6, 0x86, 0xe0, 0, 0, 0);
        Require(code[light + 22] == code[radius + 19] && code[light + 28] == code[light + 35], "light frame flow");
        var intensity = Number(U32(code, light + 4));
        var blue = Immediate(light + 29);
        Require(intensity == blue, "light channel equality");

        var projection = One(cameraStart, camera, "projection",
            0xf3, 0x0f, 0x10, 0x05, -1, -1, -1, -1,
            0xf3, 0x0f, 0x59, 0x05, -1, -1, -1, -1, 0xf3, 0x0f, 0x11, 0x4d, -1,
            0xf3, 0x0f, 0x59, 0x05, -1, -1, -1, -1, 0x0f, 0x5a, 0xc0, 0xe8, -1, -1, -1, -1,
            0xf3, 0x0f, 0x10, 0x0d, -1, -1, -1, -1, 0x8d, 0x45, -1, 0x0f, 0x57, 0xdb,
            0xc6, 0x45, -1, 0, 0xf2, 0x0f, 0x5a, 0xd8, 0x50, 0x8b, 0xce, 0x0f, 0x28, 0xd3,
            0x0f, 0x57, 0x15, -1, -1, -1, -1, 0x0f, 0x28, 0xc2,
            0xf3, 0x0f, 0x59, 0x55, -1, 0xf3, 0x0f, 0x59, 0xc1, 0xf3, 0x0f, 0x59, 0xd1,
            0xf3, 0x0f, 0x11, 0x45, -1, 0x0f, 0x28, 0xc3, 0xf3, 0x0f, 0x59, 0x5d, -1,
            0xf3, 0x0f, 0x59, 0xc1, 0xf3, 0x0f, 0x59, 0xd9,
            0xf3, 0x0f, 0x11, 0x45, -1, 0xf3, 0x0f, 0x11, 0x55, -1,
            0xf3, 0x0f, 0x11, 0x5d, -1, 0xe8, -1, -1, -1, -1);
        var aspectSlot = code[projection + 20];
        var frustumSlot = code[projection + 47];
        Require(code[projection + 79] == aspectSlot && code[projection + 100] == aspectSlot &&
            code[projection + 92] == frustumSlot && code[projection + 113] == unchecked((byte)(frustumSlot + 4)) &&
            code[projection + 118] == unchecked((byte)(frustumSlot + 12)) &&
            code[projection + 123] == unchecked((byte)(frustumSlot + 8)), "projection frame flow");
        Require(read(U32(code, projection + 68), 16).Chunk(4).All(bytes => U32(bytes, 0) == 0x80000000), "projection sign mask");

        // Both default and explicit allocation factories publish this same menu
        // object and call the same model/camera owners. Only a proven literal
        // store supplies the default; argument-forwarding factories are distinct.
        var modelStarts = SpecialBookMatches(code, Math.Max(0, model - 128), model,
            [0x53, 0x8b, 0xdc, 0x83, 0xec, 8, 0x83, 0xe4, 0xf0, 0x83, 0xc4, 4,
            0x55, 0x8b, 0x6b, 4, 0x89, 0x6c, 0x24, 4, 0x8b, 0xec, 0x6a, 0xff]);
        Require(modelStarts.Count == 1, "aligned model owner");
        var budgets = new List<int>();
        foreach (var menu in menus)
        {
            var nextMenu = menus.Where(offset => offset > menu).DefaultIfEmpty(code.Length).Min();
            var factoryEnd = Math.Min(nextMenu, Math.Min(code.Length, menu + 256));
            var typedFactory = SpecialBookMatches(code, menu, Math.Min(factoryEnd, menu + 64),
                [0x68, -1, -1, -1, -1, 0x8b, 0x89, 0x9c, 0, 0, 0, 0xe8, -1, -1, -1, -1,
                0x8b, 0xf8, 0x8b, 0xcf, 0xe8, -1, -1, -1, -1, 0x8b, 0xf0, 0x85, 0xf6,
                0x0f, 0x84, -1, -1, -1, -1, 0x8b, 0x16, 0x8b, 0xce, 0xff, 0x52, 0x34,
                0x3d, 0x24, 4, 0, 0, 0x0f, 0x85, -1, -1, -1, -1]);
            if (typedFactory.Count != 1 || typedFactory[0] != menu) continue;
            foreach (var budget in SpecialBookMatches(code, menu + 53, factoryEnd,
                         [0xc7, 0x46, 0x58, -1, -1, -1, -1, 0x8b, 0x0d, -1, -1, -1, -1,
                         0xe8, -1, -1, -1, -1, 0x8b, 0x0d, -1, -1, -1, -1, 0xe8, -1, -1, -1, -1]))
            {
                if (U32(code, budget + 9) != owner || U32(code, budget + 20) != owner ||
                    budget + 18L + unchecked((int)U32(code, budget + 14)) != modelStarts[0] ||
                    budget + 29L + unchecked((int)U32(code, budget + 25)) != cameraStart) continue;
                var publications = SpecialBookMatches(code, menu + 53, budget, [0x89, 0x35, -1, -1, -1, -1]);
                if (publications.Count != 1 || U32(code, publications[0] + 2) != owner) continue;
                budgets.Add(budget);
            }
        }
        var defaults = budgets.Distinct().ToArray();
        Require(defaults.Length == 1, "default budget consumer");
        return ([MathF.Abs(Number(U32(code, pose + 4))), Immediate(pose + 21), Immediate(pose + 31)],
            [intensity, intensity, blue], [Number(U32(code, radius + 9))], [Number(U32(code, projection + 41))],
            [Number(U32(code, projection + 12)), Number(U32(code, projection + 25))], unchecked((int)U32(code, defaults[0] + 3)));
    }

    private static List<int> SpecialBookMatches(byte[] code, int start, int end, int[] pattern)
    {
        var result = new List<int>();
        for (var at = start; at <= end - pattern.Length; at++)
        {
            var matches = true;
            for (var index = 0; index < pattern.Length && matches; index++)
                matches = pattern[index] < 0 || code[at + index] == pattern[index];
            if (matches) result.Add(at);
        }
        return result;
    }
}
