using Npgsql;

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: PgConnect <postgres-connection-url>");
    return 1;
}

var connectionString = BuildConnectionString(args[0]);

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = "select current_database(), current_user, version()";

await using var reader = await command.ExecuteReaderAsync();
if (!await reader.ReadAsync())
{
    Console.Error.WriteLine("Connected, but no result returned.");
    return 1;
}

Console.WriteLine($"Database: {reader.GetString(0)}");
Console.WriteLine($"User: {reader.GetString(1)}");
Console.WriteLine($"Version: {reader.GetString(2)}");
return 0;

static string BuildConnectionString(string rawInput)
{
    if (!Uri.TryCreate(rawInput, UriKind.Absolute, out var uri))
        return rawInput;

    if (!string.Equals(uri.Scheme, "postgresql", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(uri.Scheme, "postgres", StringComparison.OrdinalIgnoreCase))
        return rawInput;

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = uri.AbsolutePath.TrimStart('/'),
    };

    if (!string.IsNullOrWhiteSpace(uri.UserInfo))
    {
        var parts = uri.UserInfo.Split(':', 2);
        builder.Username = Uri.UnescapeDataString(parts[0]);
        if (parts.Length > 1)
            builder.Password = Uri.UnescapeDataString(parts[1]);
    }

    var query = ParseQuery(uri.Query);
    if (query.TryGetValue("sslmode", out var sslMode) && !string.IsNullOrWhiteSpace(sslMode))
        builder.SslMode = Enum.Parse<SslMode>(sslMode, ignoreCase: true);
    if (query.TryGetValue("channel_binding", out var channelBinding) && !string.IsNullOrWhiteSpace(channelBinding))
        builder.ChannelBinding = Enum.Parse<ChannelBinding>(channelBinding, ignoreCase: true);

    return builder.ConnectionString;
}

static Dictionary<string, string> ParseQuery(string query)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    if (string.IsNullOrWhiteSpace(query))
        return result;

    foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var pieces = part.Split('=', 2);
        var key = Uri.UnescapeDataString(pieces[0]);
        var value = pieces.Length > 1 ? Uri.UnescapeDataString(pieces[1]) : string.Empty;
        result[key] = value;
    }

    return result;
}
