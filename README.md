![plot](assets/icon.png)

# SqlMcp

> Give AI agents accurate SQL database schema and safe data access. No more schema guessing.

Agents working on a codebase only see the code — not the live database.
When they need schema or data, they guess, leading to wrong column names, wrong types, and missed foreign keys.

SqlMcp connects any MCP-compatible AI agent directly to your database.
Use a read-only database user for read-only access — SqlMcp does not enforce statement-level permissions.

## Supported Databases

| Database | Connection URI |
|---|---|
| **PostgreSQL** | `postgres://user:pass@host:5432/db` (or `postgresql://`) |
| **MySQL** | `mysql://user:pass@host:3306/db` |
| **SQLite** | `sqlite:./path/to/file.db` (or `file:./path` or just `*.db`/`*.sqlite`/`*.sqlite3`) |
| **SQL Server** | `mssql://user:pass@host:1433/db` |
| **Oracle** | `oracle://user:pass@host:1521/sid_or_service` |

The driver is auto-detected from the URI scheme.


## Get It as a .NET Tool

[![NuGet](https://img.shields.io/nuget/v/SqlMcp.svg)](https://www.nuget.org/packages/SqlMcp/)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://www.nuget.org/packages/SqlMcp/)

```bash
dotnet tool install -g SqlMcp
```

## Quick Start

```bash
# PostgreSQL
sqlmcp --db 'postgres://user:pass@localhost:5432/db'

# MySQL
sqlmcp --db 'mysql://user:pass@localhost:3306/db'

# SQLite
sqlmcp --db 'sqlite:./path/to/db.sqlite'

# SQL Server
sqlmcp --db 'mssql://user:pass@localhost:1433/db'

# Oracle
sqlmcp --db 'oracle://user:pass@localhost:1521/service_name'
sqlmcp --db 'oracle://user:pass@localhost:1521//sid'
```

## Configuration

| Flag | Default | Description |
| :--- | :--- | :--- |
| `--db <uri>` | `$DATABASE_URL` | Database connection URI, falls back to the `DATABASE_URL` environment variable |
| `--ssl` | false | Enable SSL/TLS for connection |

If your project already exports `DATABASE_URL`, SqlMcp picks it up without further configuration:

```bash
export DATABASE_URL='postgres://user:pass@localhost:5432/db'
sqlmcp
```

> [!WARNING]
> `DATABASE_URL` usually holds your application's user, including write permissions.
> Pass `--db` with a read-only user if the agent should not modify data.

## Available Tools

### `query`

Run a read-only SQL statement.

| Input | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `sql` | `string` | required | SELECT, SHOW, DESCRIBE, or EXPLAIN |

> [!TIP]
> Large result sets (>100 rows) are automatically written to CSV file `query_result.csv` instead of returned inline.
> The tool signals this by omitting `rows` and setting `filePath` instead.

| Output | Type | Description |
| :--- | :--- | :--- |
| `columns` | `string[]` | Column names |
| `rows` | `object[][]` | Positional row data (only when ≤100 rows) |
| `rowCount` | `int` | Total number of rows returned |
| `filePath` | `string?` | Path to CSV file when result was too large for inline delivery |
| `error` | `ErrorInfo?` | Error if the call failed |

### `execute`

Run a modifying SQL statement.

| Input | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `sql` | `string` | required | INSERT, UPDATE, DELETE, ALTER, CREATE, DROP, or TRUNCATE |

| Output | Type | Description |
| :--- | :--- | :--- |
| `affectedRows` | `int` | Number of affected rows |
| `error` | `ErrorInfo?` | Error if the call failed |

### `analyze_query`

EXPLAIN a query plan. Set `execute=true` for actual timings (SELECT only).

| Input | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `sql` | `string` | required | SQL to analyze |
| `execute` | `bool` | `false` | Run the query for actual timings (SELECT only) |
| `timeout_ms` | `int` | `5000` | Timeout in milliseconds |

| Output | Type | Description |
| :--- | :--- | :--- |
| `executed` | `bool` | Whether the query was actually executed |
| `timedOut` | `bool` | Whether the analysis timed out |
| `raw` | `string` | Plan output text |
| `error` | `ErrorInfo?` | Error if the call failed |

### `list_tables`

All tables and views in the database.

No input parameters.

| Output | Type | Description |
| :--- | :--- | :--- |
| `dialect` | `string` | Database dialect name |
| `tables` | `string[]` | Table names |
| `views` | `string[]` | View names |
| `error` | `ErrorInfo?` | Error if the call failed |

### `describe_table`

Full schema for a table.

| Input | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `table_name` | `string` | required | Table name |

| Output | Type | Description |
| :--- | :--- | :--- |
| `table` | `TableDescription` | Columns, indexes, foreign keys |
| `error` | `ErrorInfo?` | Error if the call failed |

`TableDescription` contains `name`, `columns` (`ColumnInfo[]`), `indexes` (`IndexInfo[]`), and `foreignKeys` (`ForeignKeyInfo[]`).

### `ErrorInfo`

Returned in the `error` field when a tool call fails.

| Field | Type | Description |
| :--- | :--- | :--- |
| `message` | `string` | Human-readable error |
| `details` | `Dictionary<string,string>?` | Structured context (parameter values, etc.) |

## Security

The database connection user is the sole security boundary — use a read-only user for read-only access.
