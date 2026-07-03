using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sl4n.Testing;

/// <summary>DI helpers for wiring a <see cref="SpyTransport"/> in tests.</summary>
public static class SpyTransportExtensions
{
    /// <summary>
    /// Replaces every registered <see cref="ITransport"/> with <paramref name="spy"/> so tests capture
    /// entries and produce no console noise. Call <b>after</b> <c>AddSl4n(...)</c>.
    /// </summary>
    public static IServiceCollection UseSpyTransport(this IServiceCollection services, SpyTransport spy)
    {
        services.RemoveAll<ITransport>();
        services.AddSingleton<ITransport>(spy);
        return services;
    }
}
