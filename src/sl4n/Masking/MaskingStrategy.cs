namespace Sl4n;

/// <summary>How a value is redacted once its field name matches a masking rule.</summary>
public enum MaskingStrategy
{
    /// <summary>Replace the whole value with asterisks — <c>hunter2</c> becomes <c>*******</c>.</summary>
    FullMask,

    /// <summary>Keep the first and last character of the local part — <c>j**n@example.com</c>.</summary>
    Email,

    /// <summary>Keep the last four characters — <c>************1234</c>.</summary>
    LastFour,

    /// <summary>
    /// Apply the rule's own function. A rule declared from configuration cannot carry one, so it is
    /// treated as <see cref="FullMask"/> (over-mask rather than under-mask); construct a
    /// <see cref="MaskingRule"/> directly to supply the function.
    /// </summary>
    Custom
}
