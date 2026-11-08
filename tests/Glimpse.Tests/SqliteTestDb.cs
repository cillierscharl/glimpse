using Glimpse.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Tests;

/// <summary>
/// In-memory SQLite database. Unlike the EF InMemory provider this runs real SQL, so LIKE semantics and
/// DateTime comparisons behave as they do in the app.
/// </summary>
internal sealed class SqliteTestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteTestDb()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public GlimpseDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<GlimpseDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
