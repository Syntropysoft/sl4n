using System.Text.RegularExpressions;

namespace Sl4n;

/// <summary>
/// The field-name patterns behind the built-in masking rules, as source-generated regexes — no
/// runtime compilation, AOT-safe, and linear (not subject to the ReDoS timeout). Public so you can
/// reuse one when declaring a rule of your own.
/// </summary>
public static partial class MaskingPatterns
{
    [GeneratedRegex(@"^(email|mail)$", RegexOptions.IgnoreCase)]
    public static partial Regex EmailField();

    [GeneratedRegex(@"^(password|pass|pwd|secret)$", RegexOptions.IgnoreCase)]
    public static partial Regex PasswordField();

    [GeneratedRegex(@"^(token|key|auth|jwt|bearer)$", RegexOptions.IgnoreCase)]
    public static partial Regex TokenField();

    [GeneratedRegex(@"^(credit_?card|card_?number)$", RegexOptions.IgnoreCase)]
    public static partial Regex CreditCardField();

    [GeneratedRegex(@"^(ssn|social_?security)$", RegexOptions.IgnoreCase)]
    public static partial Regex SsnField();

    [GeneratedRegex(@"^(phone|mobile|tel)$", RegexOptions.IgnoreCase)]
    public static partial Regex PhoneField();
}
