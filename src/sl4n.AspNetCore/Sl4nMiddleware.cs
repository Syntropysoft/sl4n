using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sl4n.AspNetCore;

/// <summary>
/// Extracts context fields from the inbound headers declared for <see cref="ContextConfig.Source"/>,
/// generates the ones listed in <see cref="ContextConfig.AutoGenerate"/> that did not arrive, and
/// opens a logging scope for the rest of the request — so every log in the call chain carries them
/// with no call site involved. Optionally writes them back as response headers.
/// </summary>
public sealed class Sl4nMiddleware : IMiddleware
{
    private readonly IOptions<Sl4nConfig>    _options;
    private readonly ILogger<Sl4nMiddleware> _logger;

    /// <param name="options">The sl4n configuration, for the context maps.</param>
    /// <param name="logger">Used to open the scope the request's logs inherit.</param>
    public Sl4nMiddleware(IOptions<Sl4nConfig> options, ILogger<Sl4nMiddleware> logger)
    {
        _options = options;
        _logger  = logger;
    }

    /// <summary>Establishes the request context, then invokes the rest of the pipeline.</summary>
    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        ContextConfig context = _options.Value.Context;

        // Guard 1 — no inbound config for this source and no auto-generate: nothing to do
        bool hasInbound = context.Inbound.ContainsKey(context.Source);
        bool hasAutoGenerate = context.AutoGenerate.Count > 0;

        if (!hasInbound && !hasAutoGenerate)
        {
            await next(httpContext);
            return;
        }

        // Extract fields from inbound headers
        Dictionary<string, string> fields;
        if (hasInbound)
        {
            ImmutableDictionary<string, string> requestHeaders = httpContext.Request.Headers
                .ToImmutableDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());

            fields = new Dictionary<string, string>(
                Sl4nContext.ExtractInbound(requestHeaders, context.Source, context));
        }
        else
        {
            fields = new();
        }

        // Auto-generate missing fields declared in AutoGenerate
        foreach (string field in context.AutoGenerate)
        {
            if (!fields.ContainsKey(field))
                fields[field] = Guid.NewGuid().ToString("D");
        }

        // Guard 2 — still nothing after extraction + auto-generate: skip
        if (fields.Count == 0)
        {
            await next(httpContext);
            return;
        }

        // Pipeline de propagación — AsyncLocal para headers salientes
        using Sl4nScope propagationScope = Sl4nContext.Push(fields);

        // Pipeline de log — MEL scope para enriquecimiento automático de logs
        using IDisposable logScope = _logger.BeginScope(fields)!;

        // Response headers — inject context fields using the configured response target
        if (!string.IsNullOrEmpty(context.ResponseTarget))
        {
            IReadOnlyDictionary<string, string> responseHeaders =
                Sl4nContext.GetPropagationHeaders(context.ResponseTarget, context);

            foreach (KeyValuePair<string, string> header in responseHeaders)
                httpContext.Response.Headers[header.Key] = header.Value;
        }

        await next(httpContext);
    }
}
