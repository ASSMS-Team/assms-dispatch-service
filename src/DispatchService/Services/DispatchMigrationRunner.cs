using DispatchService.Repositories;

namespace DispatchService.Services;

/// <summary>Applies each packaged Dispatch schema migration once, in filename order.</summary>
public sealed class DispatchMigrationRunner
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DispatchMigrationRunner(IDbConnectionFactory connectionFactory) => _connectionFactory = connectionFactory;

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var migrationDirectory = Path.Combine(AppContext.BaseDirectory, "migrations");
        if (!Directory.Exists(migrationDirectory))
            throw new DirectoryNotFoundException($"Dispatch migrations were not published to '{migrationDirectory}'.");

        var migrationFiles = Directory.GetFiles(migrationDirectory, "V*__*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var createHistory = connection.CreateCommand())
        {
            createHistory.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    filename VARCHAR(255) NOT NULL PRIMARY KEY,
                    applied_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                );
                """;
            await createHistory.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var migrationFile in migrationFiles)
        {
            var filename = Path.GetFileName(migrationFile);

            await using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE filename = @filename;";
            check.Parameters.AddWithValue("@filename", filename);
            var alreadyApplied = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0;
            if (alreadyApplied) continue;

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using (var apply = connection.CreateCommand())
                {
                    apply.Transaction = transaction;
                    apply.CommandText = await File.ReadAllTextAsync(migrationFile, cancellationToken);
                    await apply.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var record = connection.CreateCommand())
                {
                    record.Transaction = transaction;
                    record.CommandText = "INSERT INTO schema_migrations (filename) VALUES (@filename);";
                    record.Parameters.AddWithValue("@filename", filename);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
