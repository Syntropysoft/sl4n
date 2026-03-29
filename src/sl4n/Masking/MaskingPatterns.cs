using System.Text.RegularExpressions;

namespace Sl4n;

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
