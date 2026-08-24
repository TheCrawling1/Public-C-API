using ApiRouter.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApiRouter.Tests.Integration;

/// <summary>
/// Boots the real application in-process for integration tests, but swaps the SQLite
/// file for a private in-memory database (kept alive by the open connection) so tests
/// are isolated and leave no <c>router.db</c> behind. Startup still runs migrations and
/// seeding, so the demo admin user and seeded targets/rules exist as in production.
/// </summary>
public class ApiRouterFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        // Development so HTTPS redirection doesn't interfere with the in-process test client.
        builder.UseEnvironment("Development");

        // Keep test output readable — drop the app's request/info logging.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<RouterDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<RouterDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
