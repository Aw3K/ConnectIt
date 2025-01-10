using MySqlConnector;
using System.Data;

namespace ConnectIt;

public partial class ConnectIt
{
    public static async Task<MySqlConnection> ConnectAsync()
    {
        MySqlConnection connection = new MySqlConnection(DatabaseCredentials);
        await connection.OpenAsync();
        return connection;
    }

    public void ExecuteAsync(string query, bool reloadServers)
    {
        Task.Run(async () => {
            using var connection = await ConnectAsync();
            await using (MySqlCommand myCmd = new MySqlCommand(query, connection))
            {
                myCmd.CommandType = CommandType.Text;
                await myCmd.ExecuteNonQueryAsync();
                if (reloadServers) sendMessage("RELOAD_SERVERS_LIST", 0);
            }
        });
    }

    public async Task<List<serverInstance>> GetOtherServersAsync()
    {
        var tmp = new List<serverInstance>();

        using var connection = await ConnectAsync();
        using var query = new MySqlCommand($"SELECT `server_id`,`address`,`port`,`secure_token`,(SELECT `name` FROM `levelranks`.`lvl_web_servers` WHERE `id` = `connectit`.`server_id`) AS 'name' FROM `servers`.`connectit_servers` AS connectit WHERE `server_id` != {serverId};", connection);
        using var result = await query.ExecuteReaderAsync();

        while (await result.ReadAsync())
        {
            string address = result.GetString(1);
            int port = result.GetInt32(2);
            string secureToken = result.GetString(3);
            string name = result.GetString(4);

            if (result.GetInt32(0) > 0 && !string.IsNullOrEmpty(address) && port is >= 40000 and <= 65000 && !string.IsNullOrEmpty(secureToken) && !string.IsNullOrEmpty(name))
                tmp.Add(new serverInstance(address, port, secureToken, name));
        }
        return tmp;
    }
}
