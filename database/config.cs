using Microsoft.Extensions.Logging;
using MySqlConnector;
using Newtonsoft.Json;

namespace ConnectIt;

public partial class ConnectIt
{
    public uint serverId = 0;
    public static string DatabaseCredentials { get; set; } = string.Empty;

    private class DatabaseConfig
    {
        public string? Server { get; set; }
        public uint Port { get; set; }
        public string? UserID { get; set; }
        public string? Password { get; set; }
        public string? Database { get; set; }
    }

    private class serverID { 
        public uint id { get; set; }
    }

    private async Task LoadDatabaseCredentialsAsync()
    {
        string configFilePath = "/home/container/game/csgo/addons/counterstrikesharp/configs/database.json";
        if (!File.Exists(configFilePath))
        {
            Logger.LogCritical("Database configuration file not found.");
            return;
        }
        try
        {
            string json = await File.ReadAllTextAsync(configFilePath);
            var config = JsonConvert.DeserializeObject<DatabaseConfig>(json);

            if (config != null)
            {
                MySqlConnectionStringBuilder builder =
                    new()
                    {
                        Server = config.Server,
                        Database = config.Database,
                        UserID = config.UserID,
                        Password = config.Password,
                        Port = config.Port,
                        Pooling = true,
                        MinimumPoolSize = 0,
                        MaximumPoolSize = 640,
                        ConnectionIdleTimeout = 30,
                        AllowZeroDateTime = true
                    };
                DatabaseCredentials = builder.ConnectionString;
                Logger.LogInformation("Loaded database configuration");
            }
            else
            {
                Logger.LogCritical("Failed to parse database configuration file.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogCritical($"Error loading database configuration: {ex.Message}");
        }
    }

    private async Task LoadServerIDAsync()
    {
        string idFilePath = "/home/container/game/csgo/addons/counterstrikesharp/configs/id.json";
        if (!File.Exists(idFilePath))
        {
            Logger.LogCritical("Server ID configuration file not found.");
            return;
        }
        try
        {
            string json = await File.ReadAllTextAsync(idFilePath);
            var config = JsonConvert.DeserializeObject<serverID>(json);

            if (config != null)
            {
                serverId = config.id;
                Logger.LogInformation($"Loaded server ID: {serverId}");
            }
            else
            {
                Logger.LogCritical("Failed to parse server ID file.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogCritical($"Error loading Server ID: {ex.Message}");
        }
    }
}
