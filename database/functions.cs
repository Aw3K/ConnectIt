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

    public void ExecuteAsync(string query)
    {
        Task.Run(async () => {
            using var connection = await ConnectAsync();
            await using (MySqlCommand myCmd = new MySqlCommand(query, connection))
            {
                myCmd.CommandType = CommandType.Text;
                await myCmd.ExecuteNonQueryAsync();
                sendMessage("RELOAD_SERVERS_LIST", false);
            }
        });
    }

    public async Task<List<serverInstance>> GetOtherServersAsync()
    {
        var tmp = new List<serverInstance>();

        using var connection = await ConnectAsync();
        using var query = new MySqlCommand($"SELECT `server_id`,`address`,`port`,`secure_token` FROM `connectit_servers` WHERE `server_id` != {serverId};", connection);
        using var result = await query.ExecuteReaderAsync();

        while (await result.ReadAsync())
        {
            string address = result.GetString(1);
            int port = result.GetInt32(2);
            string secureToken = result.GetString(3);

            if (result.GetInt32(0) > 0 && !string.IsNullOrEmpty(address) && port is >= 40000 and <= 65000 && !string.IsNullOrEmpty(secureToken))
                tmp.Add(new serverInstance(address, port, secureToken));
        }
        return tmp;
    }
}
