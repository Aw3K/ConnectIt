using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace ConnectIt
{
    public partial class ConnectIt : BasePlugin
    {
        public override string ModuleName => "ConnectIt";
        public override string ModuleDescription => "Allows for servers communication with sockets.";
        public override string ModuleAuthor => "NyggaBytes";
        public override string ModuleVersion => "1.0.6";

        public class serverInstance(String address, int port, String secureToken)
        {
            public String Address = address;
            public int Port = port;
            public String SecureToken = secureToken;
        }

        public IPAddress? bindIPAddress { get; set; }
        public required Socket listener;
        public int port = 0;
        public String SecureToken = "";
        public CancellationToken listenerWaiting;
        public CancellationTokenSource source = new CancellationTokenSource();
        public Task? listenerTask;
        public List<serverInstance> otherServers = new List<serverInstance>();

        public override void Load(bool hotReload)
        {
            Logger.LogInformation($"Plugin version: {ModuleVersion}");
            otherServers.Clear();
            bindIPAddress = findLocalIpv6Address();
            listenerWaiting = source.Token;

            if (bindIPAddress != null) {
                Random random = new Random();
                port = random.Next(40000, 65000);
                SecureToken = Guid.NewGuid().ToString();
                Logger.LogInformation($"Found local IPv6 Address '{bindIPAddress.ToString()}'");
                AddTimer(3600, async () => {
                    if (serverId > 0) {
                        otherServers = await GetOtherServersAsync();
                        Logger.LogInformation($"Reloaded {otherServers.Count} servers from database");
                    }
                }, TimerFlags.REPEAT);
                if (SecureToken.Length > 0) Logger.LogInformation($"Generated secure token: '{SecureToken}'");
                Task.Run(async () => {
                    await LoadDatabaseCredentialsAsync();
                    await LoadServerIDAsync();
                    if (serverId > 0) ExecuteAsync($"INSERT INTO `connectit_servers` (`server_id`,`address`,`port`,`secure_token`) VALUES ({serverId},'{MySqlHelper.EscapeString(bindIPAddress.ToString())}',{port},'{MySqlHelper.EscapeString(SecureToken)}') ON DUPLICATE KEY UPDATE `address` = VALUES(`address`),`port` = VALUES(`port`),`secure_token` = VALUES(`secure_token`);");
                    otherServers = await GetOtherServersAsync();
                    if (otherServers.Count < 1) Logger.LogCritical("No other servers in database, Sockets not needed");
                    else Logger.LogInformation($"Loaded {otherServers.Count} other servers");
                }).Wait();
                listenerTask = Task.Run(async () => {
                    if (otherServers.Count < 1) return;
                    IPEndPoint ipEndPoint = new IPEndPoint(bindIPAddress, port);
                    listener = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        listener.Bind(ipEndPoint);
                        listener.Listen(10);
                        await Server.NextFrameAsync(() =>
                        {
                            Logger.LogInformation($"Socket bound to '{bindIPAddress}:{port}'");
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.LogCritical($"Error binding socket: {ex.Message}");
                        listener?.Dispose();
                        throw;
                    }

                    while (true)
                    {
                        var handler = await listener.AcceptAsync(listenerWaiting);
                        var buffer = new byte[4096];
                        var received = await handler.ReceiveAsync(buffer, SocketFlags.None, listenerWaiting);
                        var response = Encoding.UTF8.GetString(buffer, 0, received);

                        if (listenerWaiting.IsCancellationRequested) break;

                        var eom = "<|EOM|>";
                        if (response.IndexOf(eom) > -1)
                        {
                            response = response.Replace(eom, "");
                            if (response.IndexOf(SecureToken) > -1) {
                                response = response.Replace(SecureToken, "");
                                await Server.NextFrameAsync(() => {
                                    if (response != "RELOAD_SERVERS_LIST") {
                                        Logger.LogInformation($"Executing command '{response}'.");
                                        Server.ExecuteCommand(response);
                                    }
                                    else {
                                        Logger.LogInformation($"Received request to reload servers, reloading...");
                                        Task.Run( async () => {
                                            otherServers = await GetOtherServersAsync();
                                            await Server.NextFrameAsync(() => {
                                                Logger.LogInformation($"Reloaded {otherServers.Count} other servers");
                                            });
                                        });
                                    }
                                });
                            } else Logger.LogWarning($"Unauthorized connection, command won't be executed '{response}'");
                        }
                        handler.Dispose();
                    }
                }, listenerWaiting);
            }
            else Logger.LogCritical($"Couldn't find local IPv6 Address");

        }
        public override void Unload(bool hotReload)
        {
            try
            {
                source.Cancel();
                if (listenerTask != null && !listenerTask.IsFaulted && !listenerTask.IsCanceled && !listenerTask.IsCompleted) listenerTask?.Wait();
            }
            catch (Exception ex)
            {
                if (ex is not TaskCanceledException) Logger.LogWarning(ex.Message.ToString());
            }
            finally
            {
                ExecuteAsync($"INSERT INTO `connectit_servers` (`server_id`,`address`,`port`,`secure_token`) VALUES ({serverId},'',0,'') ON DUPLICATE KEY UPDATE `address` = VALUES(`address`),`port` = VALUES(`port`),`secure_token` = VALUES(`secure_token`);");
                listener.Disconnect(false);
                listener?.Dispose();
                listenerTask?.Dispose();
            }
            base.Unload(hotReload);
        }

        #region commands
        [ConsoleCommand("css_sendAll", "Send command to execute by all available servers.")]
        [CommandHelper(minArgs: 1, usage: "css_sendAll <COMMAND>", whoCanExecute: CommandUsage.SERVER_ONLY)]
        public void OnSendToAllCommand(CCSPlayerController? player, CommandInfo command)
        {
            String message = command.ArgByIndex(1);
            sendMessage(message, false, player, command);
        }

        [ConsoleCommand("css_sendAllId", "Send command to execute by all available servers with server id.")]
        [CommandHelper(minArgs: 1, usage: "css_sendAllId <COMMAND> ID WILL BE ADDED", whoCanExecute: CommandUsage.SERVER_ONLY)]
        public void OnSendToAllIdCommand(CCSPlayerController? player, CommandInfo command)
        {
            String message = command.ArgByIndex(1);
            sendMessage(message, true, player, command);
        }

        [ConsoleCommand("css_connectit", "Connect it plugin management and info.")]
        [CommandHelper(minArgs: 0, usage: "css_connectit <SVRELOAD|INFO|SERVERS>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
        public void OnConnectItCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (!AdminManager.PlayerHasPermissions(player, "@css/ban"))
            {
                player!.PrintToChat("[\u0004ConnectIt\u0001] \u0007Only administrators can use this feature.");
                return;
            }

            var mode = command.ArgByIndex(1);
            if (mode.ToLower() == "svreload")
            {
                command.ReplyToCommand("[\u0004ConnectIt\u0001] \u0007Reloading servers from database.");
                Task.Run(async () =>
                {
                    otherServers = await GetOtherServersAsync();
                    await Server.NextFrameAsync(() => {
                        command.ReplyToCommand($"[\u0004ConnectIt\u0001] \u0001Reloaded {otherServers.Count} servers.");
                    });
                });
            }
            else if (mode.ToLower() == "servers")
            {
                if (otherServers.Count < 1) {
                    command.ReplyToCommand("[\u0004ConnectIt\u0001] \u0007There isn't any other servers loaded.");
                    return;
                }
                command.ReplyToCommand("[\u0004ConnectIt\u0001] \u0001Currently loaded other servers:");
                foreach (var server in otherServers) {
                    command.ReplyToCommand($" \u0004{server.Address}\u0001:\u0004{server.Port}");
                }
                command.ReplyToCommand($"[\u0001/\u0004ConnectIt\u0001]");
            }
            else if (mode.Length < 1 || mode.ToLower() == "info") {
                command.ReplyToCommand("[\u0004ConnectIt\u0001]");
                command.ReplyToCommand($" \u0004Plugin Version\u0001: {ModuleVersion}");
                command.ReplyToCommand($" \u0004IP:PORT\u0001: {bindIPAddress?.ToString()}:{port}");
                command.ReplyToCommand($" \u0004Listening?\u0001: {listener.IsBound.ToString()}");
                command.ReplyToCommand($"[\u0001/\u0004ConnectIt\u0001]");
            }
        }
        #endregion

        #region functions
        public IPAddress? findLocalIpv6Address() {
            var ipv6Address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetworkV6 && !ip.Address.IsIPv6LinkLocal && ip.Address.ToString() != "::1")
                .Select(ip => ip.Address).First();
            IPAddress? tmp;
            if (IPAddress.TryParse(ipv6Address.ToString(), out tmp) && tmp.AddressFamily == AddressFamily.InterNetworkV6) {
                return ipv6Address;
            }
            return null;
        }

        public bool sendMessage(string message, bool sendId, CCSPlayerController? player = null, CommandInfo? command = null) {
            if (message == null || message.Length < 3)
            {
                command?.ReplyToCommand("Command in argument not specified.");
                return false;
            }
            if (otherServers.Count < 1)
            {
                command?.ReplyToCommand("No other servers available.");
                return false;
            }
            if (serverId < 1)
            {
                command?.ReplyToCommand("Couldn't determine own Id.");
                return false;
            }

            if (message == "RELOAD_SERVERS_LIST") Logger.LogInformation($"Sending servers reload request to {otherServers.Count} other servers");
            else Logger.LogInformation($"Sending command '{message}' to {otherServers.Count} other servers");
            foreach (var server in otherServers)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse(server.Address), server.Port);
                        using Socket client = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        await client.ConnectAsync(ipEndPoint);

                        var messageBytes = Encoding.UTF8.GetBytes(server.SecureToken + message + (sendId ? " " + serverId : "") + "<|EOM|>");
                        await client.SendAsync(messageBytes, SocketFlags.None);
                        client.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to send message to {server.Address}:{server.Port}. Error: {ex.Message}");
                    }
                });
            }
            return true;
        }
        #endregion
    }
}