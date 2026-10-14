using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Tests.Rail;

/// <summary>
/// Needs a disposable PostgreSQL server: set SMARTRAIL_TEST_POSTGRES to a connection string
/// that may create and drop databases. Without it the database tests are skipped.
/// </summary>
public sealed class RequiresDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "SMARTRAIL_TEST_POSTGRES";

    public RequiresDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to a PostgreSQL connection string to run the database tests.";
        }
    }
}

public sealed class RequiresDatabaseTheoryAttribute : TheoryAttribute
{
    public RequiresDatabaseTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RequiresDatabaseFactAttribute.Variable)))
        {
            Skip = $"Set {RequiresDatabaseFactAttribute.Variable} to a PostgreSQL connection string to run the database tests.";
        }
    }
}

/// <summary>One fresh, migrated and seeded database plus an API host that points at it.</summary>
public class TimetableApi : IAsyncLifetime
{
    // 2026-10-14 is a Wednesday.
    public static readonly DateOnly SeedStart = new(2026, 10, 14);
    public const int SeedDays = 7;

    private readonly string? _adminConnection = Environment.GetEnvironmentVariable(RequiresDatabaseFactAttribute.Variable);
    private readonly string _databaseName = $"smartrail_test_{Guid.NewGuid():N}";
    private WebApplicationFactory<Program>? _factory;

    public HttpClient Client { get; private set; } = null!;

    public WebApplicationFactory<Program> Factory => _factory!;

    public string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName }.ConnectionString;

    public async Task InitializeAsync()
    {
        if (_adminConnection is null)
        {
            return;
        }

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:smartrail", ConnectionString);
            ConfigureHost(builder);
        });
        Client = _factory.CreateClient();

        await MigrateAndSeedAsync();
    }

    /// <summary>A fixture can change the host, for example to trust a test token signer.</summary>
    protected virtual void ConfigureHost(IWebHostBuilder builder)
    {
    }

    /// <summary>Same steps as <c>--migrate-and-seed</c>; safe to call again.</summary>
    public async Task MigrateAndSeedAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartRailDbContext>();
        await db.Database.MigrateAsync();
        await DatabaseSetup.SeedAsync(db, SeedStart, SeedDays);
    }

    public async Task WithDbAsync(Func<SmartRailDbContext, Task> action)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<SmartRailDbContext>());
    }

    public virtual async Task DisposeAsync()
    {
        if (_factory is null)
        {
            return;
        }

        Client.Dispose();
        await _factory.DisposeAsync();

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
