using Microsoft.Extensions.Options;

namespace Sl4n;

/// <summary>
/// Attaches the current context to outgoing HTTP calls, so a correlation id crosses the process
/// boundary without any call site knowing about it. Register it on an <c>HttpClient</c> with
/// <c>.AddHttpMessageHandler&lt;Sl4nDelegatingHandler&gt;()</c>. Header names come from the
/// <see cref="ContextConfig.Outbound"/> map for the target.
/// </summary>
public sealed class Sl4nDelegatingHandler : DelegatingHandler
{
    private readonly IOptions<Sl4nConfig> _options;
    private readonly string              _target;

    /// <param name="options">The sl4n configuration, for the outbound map.</param>
    /// <param name="target">Which <see cref="ContextConfig.Outbound"/> entry names the headers. Defaults to <c>"http"</c>.</param>
    public Sl4nDelegatingHandler(IOptions<Sl4nConfig> options, string target = Sl4nFields.Targets.Http)
    {
        _options = options;
        _target  = target;
    }

    /// <summary>Adds the mapped context headers, then forwards the request.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ContextConfig context = _options.Value.Context;

        // Guard — no outbound config for this target or context empty: skip header injection
        if (!context.Outbound.ContainsKey(_target) || Sl4nContext.Current.IsEmpty)
            return await base.SendAsync(request, cancellationToken);

        IReadOnlyDictionary<string, string> headers =
            Sl4nContext.GetPropagationHeaders(_target, context);

        foreach (KeyValuePair<string, string> header in headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return await base.SendAsync(request, cancellationToken);
    }
}
