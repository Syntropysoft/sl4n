namespace Sl4n;

internal static class DefaultRules
{
    public static IReadOnlyList<MaskingRule> Build() =>
    [
        new MaskingRule(MaskingPatterns.EmailField(),      MaskingStrategy.Email),
        new MaskingRule(MaskingPatterns.PasswordField(),   MaskingStrategy.FullMask),
        new MaskingRule(MaskingPatterns.TokenField(),      MaskingStrategy.FullMask),
        new MaskingRule(MaskingPatterns.CreditCardField(), MaskingStrategy.LastFour),
        new MaskingRule(MaskingPatterns.SsnField(),        MaskingStrategy.LastFour),
        new MaskingRule(MaskingPatterns.PhoneField(),      MaskingStrategy.LastFour),
    ];
}
