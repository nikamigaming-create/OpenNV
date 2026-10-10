using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// Source reduction of the global command, current/target timer stores and
// independent audio-manager rate consumer. Calendar TimeScale is unrelated.
internal sealed record FalloutGlobalTimeSource(FalloutSourceFrameTimer Timer, string ContractSha256,
    float MinimumAudioRate, byte? InitialKeepSoundRate)
{
    private const string Contract = "global-time/v1;float32-current-and-target;set-rate-current-true;" +
        "optional-int32-nonzero-audio-byte;timer-write-before-audio-byte;audio-current-rate-minimum-quarter;" +
        "keep-audio-byte-selects-one;separate-raw-cached-counter-and-calendar;actual-native-global-consumer";
    private const string Fallout3Contract = "global-time-FO3/v1;one-required-Float32;no-optional-audio-policy-argument;" +
        "source-set-rate-current-true;finite-zero-negative-are-source-values;" +
        "independent-audio-manager-current-rate-minimum-quarter;no-FNV-override-byte;" +
        "source-voice-eligibility-and-threshold-consumers-independent;separate-raw-counter-and-calendar;actual-native-global-consumer";
    internal bool HasSoundOverride => Timer.EngineSha256 == FalloutSourceMainFamily.NewVegas;
    private static string ContractFor(string image) => image switch
    {
        FalloutSourceMainFamily.NewVegas => Contract,
        FalloutSourceMainFamily.Fallout3 => Fallout3Contract,
        _ => throw new NotSupportedException("Selected global time command/audio manager has no reduced source contract.")
    };
    internal static string CurrentContractSha256 =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Contract))).ToLowerInvariant();

    internal static FalloutGlobalTimeSource Read(FalloutAdvancementRuntimeReceipt receipt)
    {
        var timer = FalloutSourceFrameTimer.Read(receipt);
        var result = new FalloutGlobalTimeSource(timer, FalloutAdvancementRuntimeReceipt.Hash(ContractFor(timer.EngineSha256)),
            0.25f, timer.EngineSha256 == FalloutSourceMainFamily.NewVegas ? (byte?)0 : null);
        result.Validate();
        return result;
    }

    internal void Validate()
    {
        if (Timer is null) throw new InvalidDataException("Global time omitted its selected timer source.");
        Timer.Validate();
        if (ContractSha256 != FalloutAdvancementRuntimeReceipt.Hash(ContractFor(Timer.EngineSha256)) || MinimumAudioRate != 0.25f ||
            InitialKeepSoundRate != (HasSoundOverride ? (byte?)0 : null))
            throw new InvalidDataException("Global time differs from its selected command/audio constructor contract.");
    }

    internal float AudioRate(float currentRate, byte? keepSoundRate)
    {
        Validate();
        if (!float.IsFinite(currentRate) || (HasSoundOverride ? keepSoundRate is null or > 1 : keepSoundRate is not null))
            throw new InvalidDataException("Global audio rate has no supported source scalar/byte representation.");
        var clamped = MathF.Max(currentRate, MinimumAudioRate);
        return keepSoundRate == 1 ? 1f : clamped;
    }
}
