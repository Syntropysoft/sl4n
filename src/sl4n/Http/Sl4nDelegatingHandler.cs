using Microsoft.Extensions.Options;

namespace Sl4n;

public sealed class Sl4nDelegatingHandler : DelegatingHandler
{
    private readonly IOptions<Sl4nConfig> _options;
    private readonly string              _target;

    public Sl4nDelegatingHandler(IOptions<Sl4nConfig> options, string target = Sl4nFields.Targets.Http)
    {
        _options = options;
        _target  = target;
    }

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
