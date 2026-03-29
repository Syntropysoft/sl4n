using Microsoft.Extensions.Logging;

namespace Sl4n;

internal readonly struct RawLogEvent
{
    internal readonly LogLevel                               Level;
    internal readonly string                                 Category;
    internal readonly string                                 Message;
    internal readonly IEnumerable<KeyValuePair<string, object?>>? StructuredState;
    internal readonly Exception?                             Exception;
    internal readonly List<KeyValuePair<string, object?>>?   ScopeFields;

    internal RawLogEvent(
        LogLevel                                    level,
        string                                      category,
        string                                      message,
        IEnumerable<KeyValuePair<string, object?>>? structuredState,
        Exception?                                  exception,
        List<KeyValuePair<string, object?>>?        scopeFields)
    {
        Level           = level;
        Category        = category;
        Message         = message;
        StructuredState = structuredState;
        Exception       = exception;
        ScopeFields     = scopeFields;
    }
}
