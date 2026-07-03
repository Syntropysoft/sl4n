using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sl4n;

/// <summary>Helpers for choosing the transport(s) sl4n writes to. Call after <c>AddSl4n(...)</c>.</summary>
public static class Sl4nTransportExtensions
{
    /// <summary>
    /// Replaces every registered <see cref="ITransport"/> with <paramref name="transport"/> — e.g. a
    /// <see cref="DurableFileTransport"/> wrapping your real sink, so nothing else writes in parallel.
    /// </summary>
    public static IServiceCollection UseTransport(this IServiceCollection services, ITransport transport)
    {
        services.RemoveAll<ITransport>();
        services.AddSingleton(transport);
        return services;
    }

    /// <summary>Adds an additional <see cref="ITransport"/> alongside the ones already registered.</summary>
    public static IServiceCollection AddTransport(this IServiceCollection services, ITransport transport)
    {
        services.AddSingleton(transport);
        return services;
    }
}
