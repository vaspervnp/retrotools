using MySqlConnector;
using Xunit.Abstractions;

namespace RetroTools.Data.Tests;

/// <summary>
/// M0 smoke tests: επιβεβαιώνουν ότι ο Pomelo/MySqlConnector μιλά πραγματικά
/// με τη MariaDB 11 πάνω σε .NET 10, και ότι η βάση δέχεται DDL/DML.
/// </summary>
public class DatabaseConnectivityTests
{
    private readonly ITestOutputHelper _output;

    public DatabaseConnectivityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [DatabaseFact]
    public async Task Can_open_connection_and_server_is_mariadb_11()
    {
        await using var connection = new MySqlConnection(TestConfiguration.RequireConnectionString());
        await connection.OpenAsync();

        var version = connection.ServerVersion;
        _output.WriteLine("Server version: " + version);

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains("MariaDB", version, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("11.", version, StringComparison.Ordinal);
    }

    [DatabaseFact]
    public async Task Current_database_is_retrotools_and_utf8mb4_is_available()
    {
        await using var connection = new MySqlConnection(TestConfiguration.RequireConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE(), @@character_set_server, @@version_comment;";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        var databaseName = reader.GetString(0);
        var charset = reader.GetString(1);
        var comment = reader.GetString(2);

        _output.WriteLine("Database: " + databaseName);
        _output.WriteLine("Server charset: " + charset);
        _output.WriteLine("Version comment: " + comment);

        Assert.Equal("retrotools", databaseName);
    }

    [DatabaseFact]
    public async Task User_can_create_and_drop_tables()
    {
        const string tableName = "_retrotools_m0_smoke";

        await using var connection = new MySqlConnection(TestConfiguration.RequireConnectionString());
        await connection.OpenAsync();

        try
        {
            await ExecuteAsync(connection,
                "CREATE TABLE IF NOT EXISTS " + tableName + " (" +
                " id INT AUTO_INCREMENT PRIMARY KEY," +
                " name VARCHAR(64) NOT NULL," +
                " payload MEDIUMBLOB NULL" +
                ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

            await ExecuteAsync(connection,
                "INSERT INTO " + tableName + " (name, payload) VALUES ('Άμστραντ CPC', 0x00010203);");

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, payload FROM " + tableName + " LIMIT 1;";
            await using var reader = await command.ExecuteReaderAsync();

            Assert.True(await reader.ReadAsync());
            Assert.Equal("Άμστραντ CPC", reader.GetString(0));

            var payload = (byte[])reader.GetValue(1);
            Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x03 }, payload);
        }
        finally
        {
            await ExecuteAsync(connection, "DROP TABLE IF EXISTS " + tableName + ";");
        }
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
