namespace OpenNV.Runtime.Content;

internal sealed record FalloutMainUtilityCommandSource(FalloutMainUtilitySource Main, byte InitialSuppression,
    string FieldDeclarationSha256, uint SubmitEvent, int TextCapacity, uint DiagnosticReturn, string ContractSha256)
{
    private const string Contract = "source-Main-utility-command/v1;owned-command-linked-loader-byte;" +
        "independent-from-platform-third-byte-and-console-open-counter;" +
        "positive-signed-console-counter-and-current-interface-before-submit;" +
        "source-event80000008;bounded-edit-bytes-cursor-removal-and-newline-normalization;" +
        "first-normalized-byte-nonzero-latches-one-before-parser;never-open-close-or-save-completion;" +
        "selected-range-diagnostic-return-zero-source-body-no-output-or-effects;" +
        "actual-source-Main-session-owner-and-committed-prefix-no-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + FieldDeclarationSha256 + "\0" + ContractSha256);
    internal static FalloutMainUtilityCommandSource Read(FalloutMainUtilitySource main, byte loaderByte, string fieldDeclaration)
    {
        main.Validate();
        if (main.Main.EngineSha256 != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" || loaderByte != 0 ||
            !FalloutAdvancementRuntimeReceipt.Digest(fieldDeclaration))
            throw new NotSupportedException("Selected suppression/console command source has no reviewed producer declaration.");
        return new(main, loaderByte, fieldDeclaration, 0x80000008, 0x7fe, 0, FalloutAdvancementRuntimeReceipt.Hash(Contract));
    }
    internal void Validate()
    {
        if (this != Read(Main, InitialSuppression, FieldDeclarationSha256))
            throw new InvalidDataException("Main command producer changed its selected loader/UI/diagnostic contract.");
    }
}

internal sealed record FalloutPlatformStartupArgumentSource(FalloutMainUtilitySource Main, string Option,
    string DeclarationSha256, string ContractSha256)
{
    private const string Contract = "source-platform-early-startup/v1;lazy-initialization-query-return-observed-ignored;" +
        "actual-selected-source-argument-vector-order;first-UTF16-character45;ordinal-ASCII-case-insensitive-whole-suffix;" +
        "matching-source-option-calls-same-getter-before-independent-byte-zero-store;" +
        "constructor-one-no-default-ready-or-suppression-alias;duplicate-matches-remain-real-writes;" +
        "actual-constructor-call-prefix-and-entered-caller-no-replay";
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(Main.Identity + "\0" + DeclarationSha256 + "\0" +
        FalloutAdvancementRuntimeReceipt.Hash(Option) + "\0" + ContractSha256);
    internal static FalloutPlatformStartupArgumentSource Read(FalloutMainUtilitySource main, string option, string declaration)
    {
        main.Validate();
        if (main.Main.EngineSha256 != "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" ||
            string.IsNullOrEmpty(option) || option.Length > 128 || option.Any(character => character is < (char)0x21 or > (char)0x7e) ||
            !FalloutAdvancementRuntimeReceipt.Digest(declaration))
            throw new NotSupportedException("Selected source startup argument producer is unowned.");
        return new(main, option, declaration, FalloutAdvancementRuntimeReceipt.Hash(Contract));
    }
    internal void Validate()
    {
        if (this != Read(Main, Option, DeclarationSha256))
            throw new InvalidDataException("Original startup changed its selected argument/independent-field contract.");
    }
    internal bool Matches(string? actualArgument)
    {
        Validate();
        if (actualArgument is null || actualArgument.Length == 0 || actualArgument[0] != (char)45) return false;
        var suffix = actualArgument.AsSpan(1);
        if (suffix.Length != Option.Length) return false;
        foreach (var character in suffix)
            if (character > 0x7f)
                throw new NotSupportedException("Original wide startup comparison requires its selected non-ASCII locale consumer.");
        for (var index = 0; index < suffix.Length; index++)
        {
            var character = suffix[index]; var expected = Option[index];
            // The selected declaration is ASCII. Do not replace the original
            // comparison with broad Unicode folding or partial-token matches.
            if (character is >= 'A' and <= 'Z') character = (char)(character + ('a' - 'A'));
            if (expected is >= 'A' and <= 'Z') expected = (char)(expected + ('a' - 'A'));
            if (character != expected) return false;
        }
        return true;
    }
}
