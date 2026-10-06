using Microsoft.Extensions.Hosting;
using SqlMcp.Tools;

namespace SqlMcp.Host;

public static class McpServerHost
{
    internal const string DbUriEnvironmentVariable = "DATABASE_URL";

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var options = ParseOptions(args);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Services.Compose(options);

        var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static SqlMcpOptions ParseOptions(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? dbUri = null;
        var ssl = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            switch (argument)
            {
                case "--db":
                    if (dbUri is not null)
                        throw new ArgumentException("The '--db' option may only be specified once.", nameof(args));

                    if (index + 1 >= args.Length)
                        throw new ArgumentException("Missing value for '--db'. Expected '--db <connection-uri>'.", nameof(args));

                    dbUri = args[++index];
                    if (string.IsNullOrWhiteSpace(dbUri))
                        throw new ArgumentException("The '--db' value must not be empty or whitespace.", nameof(args));

                    break;

                case "--ssl":
                    ssl = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.", nameof(args));
            }
        }

        dbUri ??= Environment.GetEnvironmentVariable(DbUriEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(dbUri))
            throw new ArgumentException(
                $"Database connection URI is required. Use --db <connection-uri> or set {DbUriEnvironmentVariable}.", nameof(args));

        return new SqlMcpOptions(ConnectionUri: dbUri!, UseSsl: ssl);
    }
}
