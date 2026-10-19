namespace Workmate.Poc.Data;

/// <summary>
/// Runs before anything opens either SQLite file (domain or Elsa), because Elsa's own
/// migrations run from a hosted service that can open <c>workmate-elsa.db</c> before the
/// seeder gets a chance to reset it. Deletes both files (plus -shm/-wal) when
/// <c>Poc:ResetDatabaseOnStartup</c> is true, so domain and workflow data never get out of step.
/// </summary>
public static class PocDatabaseReset
{
    public static void Apply(IConfiguration configuration)
    {
        if (!configuration.GetValue("Poc:ResetDatabaseOnStartup", false)) return;

        DeleteSqliteFiles(configuration.GetConnectionString("Default"));
        DeleteSqliteFiles(configuration.GetConnectionString("Elsa"));
    }

    private static void DeleteSqliteFiles(string? connectionString)
    {
        var path = ExtractDataSource(connectionString);
        if (path == null) return;

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var file = path + suffix;
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static string? ExtractDataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }
}
