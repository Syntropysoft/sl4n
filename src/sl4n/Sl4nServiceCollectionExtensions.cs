using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sl4n;

public static class Sl4nServiceCollectionExtensions
{
    [RequiresUnreferencedCode("Configuration binding uses reflection. Use the Action<Sl4nConfig> overload for AOT compatibility.")]
    [RequiresDynamicCode("Configuration binding may require dynamic code generation. Use the Action<Sl4nConfig> overload for AOT compatibility.")]
    public static IServiceCollection AddSl4n(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<Sl4nConfig>(configuration);
        return services.AddSl4nCore();
    }

    public static IServiceCollection AddSl4n(
        this IServiceCollection services,
        Action<Sl4nConfig> configure)
    {
        services.Configure(configure);
        return services.AddSl4nCore();
    }

    private static IServiceCollection AddSl4nCore(this IServiceCollection services)
    {
        // Masking engine
        services.TryAddSingleton<MaskingEngine>(sp =>
        {
            MaskingConfig config = sp.GetRequiredService<IOptions<Sl4nConfig>>().Value.Masking;
            return MaskingEngine.Create(config);
        });

        // Async transport channel
        services.TryAddSingleton(_ => Sl4nChannel.Create());

        // Transport worker — owns masking + dict building
        services.TryAddSingleton<Sl4nTransportWorker>(sp =>
            new Sl4nTransportWorker(
                sp.GetRequiredService<Channel<RawLogEvent>>().Reader,
                sp.GetServices<ITransport>(),
                sp.GetRequiredService<MaskingEngine>()));

        services.AddHostedService(sp => sp.GetRequiredService<Sl4nTransportWorker>());

        // Default transport (overridable by the user)
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITransport, ConsoleTransport>());

        // Logger provider — hot path only, no masking
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.Services.TryAddSingleton<Sl4nLoggerProvider>(sp =>
                new Sl4nLoggerProvider(
                    sp.GetRequiredService<Channel<RawLogEvent>>()));
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ILoggerProvider, Sl4nLoggerProvider>(
                    sp => sp.GetRequiredService<Sl4nLoggerProvider>()));
        });

        return services;
    }
}
