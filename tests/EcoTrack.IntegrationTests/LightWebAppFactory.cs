using EcoTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EcoTrack.IntegrationTests;

/// <summary>
/// Lightweight test factory that uses an in-memory database.
/// Use for tests that verify HTTP infrastructure, routing, or non-persistence behavior.
/// Use IntegrationTestWebAppFactory for tests that require real PostgreSQL persistence.
/// </summary>
public class LightWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase("ecotrack_light_test"));
        });
    }
}
