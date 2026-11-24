using FluentValidation;

namespace Torque.Application.Common;

public static class ValidationRules
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;

    /// <summary>
    /// Length beats complexity rules: a 10+ character passphrase is stronger and easier to remember
    /// than "P@ssw0rd" (current NIST SP 800-63B guidance).
    /// </summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinPasswordLength)
            .MaximumLength(MaxPasswordLength);

    public static IRuleBuilderOptions<T, PageRequest> ValidPage<T>(this IRuleBuilder<T, PageRequest> rule) =>
        rule.Must(p => p.Page >= 1 && p.PageSize is >= 1 and <= PageRequest.MaxPageSize)
            .WithMessage($"Page must be >= 1 and pageSize between 1 and {PageRequest.MaxPageSize}.");
}
