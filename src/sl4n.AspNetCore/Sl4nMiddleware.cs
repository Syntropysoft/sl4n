using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sl4n.AspNetCore;

public sealed class Sl4nMiddleware : IMiddleware
{
    private readonly IOptions<Sl4nConfig>    _options;
    private readonly ILogger<Sl4nMiddleware> _logger;

    public Sl4nMiddleware(IOptions<Sl4nConfig> options, ILogger<Sl4nMiddleware> logger)
    {
        _options = options;
        _logger  = logger;
    }

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
