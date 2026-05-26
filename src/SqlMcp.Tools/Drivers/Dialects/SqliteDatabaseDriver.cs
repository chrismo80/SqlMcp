using System.Data;
using Microsoft.Data.Sqlite;
using SqlMcp.Tools.Models;

namespace SqlMcp.Tools.Drivers.Dialects;

internal sealed class SqliteDatabaseDriver : IDatabaseDriver
{
    public static IDatabaseDriver Create(string uri)
    {
        var path = uri;
        path = path.Replace("sqlite:", "", StringComparison.OrdinalIgnoreCase);
        path = path.Replace("file:", "", StringComparison.OrdinalIgnoreCase);
        path = path.Trim();

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("SQLite path must not be empty.", nameof(uri));

        var cs = path.Contains('=')
            ? path
            : new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();

        return new SqliteDatabaseDriver(cs);
    }

    private readonly string _connectionString;

    private SqliteDatabaseDriver(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbDialect Dialect => DbDialect.Sqlite;

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT name, type FROM sqlite_master
WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%'
ORDER BY name";

        var result = new List<TableInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            result.Add(new TableInfo(name, string.Equals(type, "view", StringComparison.OrdinalIgnoreCase) ? DbTableType.View : DbTableType.Table));
        }
        return result;
    }

    public async Task<TableDescription> DescribeTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        tableName.ValidateIdentifier();
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var columns = new List<ColumnInfo>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.GetString(reader.GetOrdinal("name"));
                var type = reader.IsDBNull(reader.GetOrdinal("type")) ? "TEXT" : reader.GetString(reader.GetOrdinal("type"));
                var notNull = reader.GetInt32(reader.GetOrdinal("notnull")) != 0;
                var def = reader.IsDBNull(reader.GetOrdinal("dflt_value")) ? null : reader.GetValue(reader.GetOrdinal("dflt_value"))?.ToString();
                var pk = reader.GetInt32(reader.GetOrdinal("pk")) != 0;

                columns.Add(new ColumnInfo(name, type, Nullable: !notNull, DefaultValue: def, IsPrimaryKey: pk, IsUnique: false, Extra: null));
            }
        }

        if (columns.Count == 0)
            throw new ArgumentException($"Table '{tableName}' does not exist.", nameof(tableName));

        var indexes = new List<IndexInfo>();
        var uniqueCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA index_list(\"{tableName}\")";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var idxName = reader.GetString(reader.GetOrdinal("name"));
                var unique = reader.GetInt32(reader.GetOrdinal("unique")) == 1;

                var cols = await ReadIndexColumnsAsync(conn, idxName, cancellationToken).ConfigureAwait(false);
                indexes.Add(new IndexInfo(idxName, unique, cols));

                if (unique && cols.Count == 1)
                    uniqueCols.Add(cols[0]);
            }
        }

        columns = columns.Select(c => c with { IsUnique = !c.IsPrimaryKey && uniqueCols.Contains(c.Name) }).ToList();

        var foreignKeys = new List<ForeignKeyInfo>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA foreign_key_list(\"{tableName}\")";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt32(reader.GetOrdinal("id"));
                foreignKeys.Add(new ForeignKeyInfo(
                    ConstraintName: $"fk_{tableName}_{id}",
                    Column: reader.GetString(reader.GetOrdinal("from")),
                    ReferencedTable: reader.GetString(reader.GetOrdinal("table")),
                    ReferencedColumn: reader.GetString(reader.GetOrdinal("to"))));
            }
        }

        return new TableDescription(tableName, columns, indexes, foreignKeys);
    }

    public async Task<QueryResult> QueryAsync(string sql, int limit, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadResultAsync(limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExecutionResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new ExecutionResult(affected);
    }

    public async Task<AnalyzeResult> AnalyzeQueryAsync(string sql, bool execute, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var stripped = sql.Trim().TrimEnd(';');

        await using var conn = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"EXPLAIN QUERY PLAN {stripped}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cts.Token).ConfigureAwait(false);
            var lines = new List<string>();
            while (await reader.ReadAsync(cts.Token).ConfigureAwait(false))
                lines.Add(reader.GetString(reader.GetOrdinal("detail")));

            var raw = string.Join("\n", lines);

            if (execute)
                raw += "\n\n(Note: SQLite does not support EXPLAIN ANALYZE. Showing plan-only output.)";

            return new AnalyzeResult(raw, Executed: false);
        }
        catch (OperationCanceledException)
        {
            return new AnalyzeResult(string.Empty, Executed: false, TimedOut: true);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return conn;
    }

    private static async Task<IReadOnlyList<string>> ReadIndexColumnsAsync(SqliteConnection conn, string indexName, CancellationToken ct)
    {
        var cols = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA index_info(\"{indexName}\")";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            cols.Add(reader.GetString(reader.GetOrdinal("name")));
        return cols;
    }
}
