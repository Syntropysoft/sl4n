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

        // Guard 1 — no inbound config for this source: skip header parsing entirely
        if (!context.Inbound.ContainsKey(context.Source))
        {
            await next(httpContext);
            return;
        }

        ImmutableDictionary<string, string> requestHeaders = httpContext.Request.Headers
            .ToImmutableDictionary(h => h.Key.ToLowerInvariant(), h => h.Value.ToString());

        IReadOnlyDictionary<string, string> fields = Sl4nContext.ExtractInbound(
            requestHeaders, context.Source, context);

        // Guard 2 — no headers matched: skip Push and BeginScope
        if (fields.Count == 0)
        {
            await next(httpContext);
            return;
        }

        // Pipeline de propagación — AsyncLocal para headers salientes
        using Sl4nScope propagationScope = Sl4nContext.Push(fields);

        // Pipeline de log — MEL scope para enriquecimiento automático de logs
        using IDisposable logScope = _logger.BeginScope(fields)!;

        await next(httpContext);
    }
}
