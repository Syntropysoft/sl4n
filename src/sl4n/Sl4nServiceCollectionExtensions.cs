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
        // Runtime counters (getStats equivalent)
        services.TryAddSingleton<Sl4nStats>();

        // Masking engine — masking failures also bump the stats counter, then the user's hook fires
        services.TryAddSingleton<MaskingEngine>(sp =>
        {
            MaskingConfig config = sp.GetRequiredService<IOptions<Sl4nConfig>>().Value.Masking;
            Sl4nStats stats = sp.GetRequiredService<Sl4nStats>();
            Action<Exception, string>? userHook = config.OnMaskingError;
            return MaskingEngine.Create(config, (ex, key) =>
            {
                stats.IncrMaskingFailures();
                userHook?.Invoke(ex, key);
            });
        });

        // Logging matrix — per-level context field whitelist
        services.TryAddSingleton<LoggingMatrix>(sp =>
            LoggingMatrix.Create(sp.GetRequiredService<IOptions<Sl4nConfig>>().Value.LoggingMatrix));

        // Retention registry — resolves policy names to compliance metadata
        services.TryAddSingleton<RetentionRegistry>(sp =>
            RetentionRegistry.Create(sp.GetRequiredService<IOptions<Sl4nConfig>>().Value.RetentionPolicies));

        // Async transport channel
        services.TryAddSingleton(_ => Sl4nChannel.Create());

        // Transport worker — owns masking, matrix filtering, sanitization + dict building
        services.TryAddSingleton<Sl4nTransportWorker>(sp =>
        {
            Sl4nConfig cfg = sp.GetRequiredService<IOptions<Sl4nConfig>>().Value;
            return new Sl4nTransportWorker(
                sp.GetRequiredService<Channel<RawLogEvent>>().Reader,
                sp.GetServices<ITransport>(),
                sp.GetRequiredService<MaskingEngine>(),
                sp.GetRequiredService<LoggingMatrix>(),
                sp.GetRequiredService<Sl4nStats>(),
                cfg.OnLogFailure,
                sp.GetRequiredService<RetentionRegistry>(),
                // Sinks registered under Sl4nTransportKeys.Unmasked. Keyed services do NOT come
                // back from GetServices<ITransport>(), so the framework hands us the two groups
                // already separated — no marker type and no reference matching of our own.
                sp.GetKeyedServices<ITransport>(Sl4nTransportKeys.Unmasked));
        });

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
