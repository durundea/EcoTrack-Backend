using EcoTrack.Application.Auth.Interfaces;
using EcoTrack.Application.Auth.Login;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory;
using EcoTrack.Infrastructure.Persistence;
using EcoTrack.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EcoTrack.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("EcoTrackDb")));

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<AppDbContext>());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<LoginService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<SalesService>();

        return services;
    }
}
