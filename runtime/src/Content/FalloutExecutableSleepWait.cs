using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSleepWaitCaptionDeclaration(string EngineSha256, string SourceSha256,
    string DateFormat, string CaptionFormat, bool Float32Minutes);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutSleepWaitCaptionDeclaration ReadSleepWaitCaption(string path)
    {
        var original = File.ReadAllBytes(path);
        var engine = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        var single = engine switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => false,
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => true,
            _ => throw new NotSupportedException("Selected source has no sleep/wait caption consumer."),
        };
        var (code, image) = Load(original);
        var captions = new HashSet<string>(StringComparer.Ordinal);
        var dates = new HashSet<string>(StringComparer.Ordinal);
        for (var at = 0; at <= code.Length - 5; ++at)
        {
            if (code[at] != 0x68 || image.Literal(U32(code, at + 1)) is not { } format) continue;
            // The selected native consumer emits five caption operands into a
            // printf call, then publishes the string tile trait. Keep the owned
            // literal instead of copying an English date into the product.
            var tokens = Regex.Matches(format, @"%(?:0[1-9][0-9]*)?[ds]")
                .Select(match => match.Value).ToArray();
            if (tokens.SequenceEqual(new[] { "%s", "%s", "%d", "%02d", "%s" }) &&
                !Regex.Replace(format, @"%(?:0[1-9][0-9]*)?[ds]", "").Contains('%'))
            {
                var end = Math.Min(code.Length, at + 180);
                if (code.AsSpan(at + 5, end - at - 5).IndexOf(new byte[] { 0x68, 0xc4, 0x0f, 0, 0 }) >= 0)
                    captions.Add(format);
            }
            if (tokens.SequenceEqual(new[] { "%02d", "%02d", "%02d" }) &&
                !Regex.Replace(format, @"%(?:0[1-9][0-9]*)?[ds]", "").Contains('%')) dates.Add(format);
        }
        if (captions.Count != 1 || dates.Count != 1)
            throw new NotSupportedException("Selected sleep/wait date/caption literal associations are missing or ambiguous.");
        var date = dates.Single(); var caption = captions.Single();
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(engine + "\0" + date + "\0" + caption + "\0" + single))).ToLowerInvariant();
        return new(engine, sha, date, caption, single);
    }
}
