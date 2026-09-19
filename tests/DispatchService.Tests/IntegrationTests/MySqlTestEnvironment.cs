namespace DispatchService.Tests.IntegrationTests;

// Where the MySQL integration tests get their database, and how they are skipped
// when there is not one.
//
// These tests need a real MySQL server: the behaviour under test is a
// Serializable transaction, a unique index and a foreign key acting together, and
// none of that exists in a fake. They are therefore opt-in rather than part of the
// default run - a developer without a database, and a CI job that has not been
// given one, must still get a green suite rather than a wall of connection
// errors.
public static class MySqlTestEnvironment
{
    // Set this to run them. For example, against the local Compose MySQL:
    //
    //   $env:ASSMS_DISPATCH_TEST_CONNECTION =
    //     "Server=localhost;Port=3306;Database=dispatchdb_test;User ID=root;Password=...;"
    //
    // Point it at a scratch database, never at dispatchdb itself: every test
    // truncates the Dispatch tables before it runs.
    public const string ConnectionStringVariable = "ASSMS_DISPATCH_TEST_CONNECTION";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public static string SkipReason =>
        $"Set {ConnectionStringVariable} to a scratch MySQL database to run the integration tests. "
        + "They delete every row in every Dispatch table, so never point it at a database holding real data.";

    // The guard that stops these tests from ever emptying a real database.
    //
    // Every test here deletes all rows from all six Dispatch tables. Pointed at
    // staging `dispatchdb` - a plausible mistake, since that connection string is
    // the one people have to hand - it would destroy the technicians, assignments
    // and outbox the staging evidence run depends on, silently and in seconds.
    //
    // Requiring the word "test" in the database name is a crude check, and
    // deliberately so: it cannot be satisfied by accident, and anyone who wants to
    // run against a differently named database has to rename it and think about
    // why. A connection string with no Database at all is refused too, because it
    // would otherwise operate on whatever the server's default happens to be.
    public static void EnsureSafeTargetDatabase()
    {
        var builder = new MySqlConnector.MySqlConnectionStringBuilder(ConnectionString!);
        var database = builder.Database;

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} names no database. Refusing to run: these tests delete every row "
                + "in every Dispatch table and must never be aimed at a default schema.");
        }

        if (database.IndexOf("test", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} points at database '{database}', whose name does not contain 'test'. "
                + "Refusing to run: these tests delete every row in every Dispatch table. Use a scratch database "
                + "such as 'dispatchdb_test'. Never point them at staging dispatchdb.");
        }
    }
}

// A Fact that skips itself when no database is configured.
//
// xunit 2.5.3 has no Assert.Skip, so the decision is made when the attribute is
// constructed - at discovery - which is early enough for the runner to report the
// test as skipped rather than run it and fail.
public sealed class RequiresMySqlFactAttribute : FactAttribute
{
    public RequiresMySqlFactAttribute()
    {
        if (!MySqlTestEnvironment.IsConfigured)
        {
            Skip = MySqlTestEnvironment.SkipReason;
        }
    }
}
