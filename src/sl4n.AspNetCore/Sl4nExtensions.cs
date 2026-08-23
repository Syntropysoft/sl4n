using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Sl4n.AspNetCore;

/// <summary>Wires sl4n's context propagation into the ASP.NET Core request pipeline.</summary>
public static class Sl4nExtensions
{
    /// <summary>
    /// Registers <see cref="Sl4nMiddleware"/>. Call alongside <c>AddSl4n(...)</c>, then
    /// <see cref="UseSl4n"/> in the pipeline.
    /// </summary>
    public static IServiceCollection AddSl4nAspNetCore(this IServiceCollection services)
    {
        services.AddTransient<Sl4nMiddleware>();
        return services;
    }

    /// <summary>
    /// Adds the middleware that reads context from inbound headers, auto-generates what is missing,
    /// and opens a scope for the request. Place it EARLY — anything logged before it runs has no
    /// correlation id.
    /// </summary>
    public static IApplicationBuilder UseSl4n(this IApplicationBuilder app)
    {
        return app.UseMiddleware<Sl4nMiddleware>();
    }
}
