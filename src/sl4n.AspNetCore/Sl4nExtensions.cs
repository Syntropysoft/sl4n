using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Sl4n.AspNetCore;

public static class Sl4nExtensions
{
    public static IServiceCollection AddSl4nAspNetCore(this IServiceCollection services)
    {
        services.AddTransient<Sl4nMiddleware>();
        return services;
    }

    public static IApplicationBuilder UseSl4n(this IApplicationBuilder app)
    {
        return app.UseMiddleware<Sl4nMiddleware>();
    }
}
