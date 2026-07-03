using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sl4n;

/// <summary>Transport-selection helpers for the console.</summary>
public static class Sl4nConsoleExtensions
{
    /// <summary>
    /// Replaces the default JSON <see cref="ConsoleTransport"/> with the human-readable
    /// <see cref="ClassicConsoleTransport"/>. Call <b>after</b> <c>AddSl4n(...)</c>. Removes any
    /// previously registered <see cref="ITransport"/> — register custom transports after this call.
    /// </summary>
    public static IServiceCollection UseClassicConsole(this IServiceCollection services)
    {
        services.RemoveAll<ITransport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITransport, ClassicConsoleTransport>());
        return services;
    }
}
