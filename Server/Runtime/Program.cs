﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Grapevine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using HttpStatusCode = Grapevine.HttpStatusCode;

namespace JFramework.Net
{
    internal class Program
    {
        public static Setting setting;
        public static Program instance;
        public static Transport transport;
        public readonly Dictionary<int, IPEndPoint> connections = new Dictionary<int, IPEndPoint>();

        private int heartBeat;
        private Service service;
        private UdpClient punchClient;
        private MethodInfo awakeMethod;
        private MethodInfo updateMethod;
        private readonly byte[] buffers = new byte[500];
        private readonly List<int> clients = new List<int>();
        private readonly HashMap<int, string> punches = new HashMap<int, string>();

        private const string SETTING = "setting.json";
        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();
        public List<Room> rooms => service.rooms.Values.ToList();

        public async Task MainAsync()
        {
            instance = this;
            Debug.Log("启动中继服务器!", ConsoleColor.Green);
            if (!File.Exists(SETTING))
            {
                await File.WriteAllTextAsync(SETTING, JsonConvert.SerializeObject(new Setting(), Formatting.Indented));
                Debug.Log("请将 setting.json 文件配置正确并重新运行!", ConsoleColor.Yellow);
                Console.ReadKey();
                Environment.Exit(0);
                return;
            }

            setting = JsonConvert.DeserializeObject<Setting>(await File.ReadAllTextAsync(SETTING));
            Debug.Log("加载程序集...", ConsoleColor.White, true);
            try
            {
                var assembly = Assembly.LoadFile(Path.GetFullPath(setting.Assembly));
                Debug.Log("OK", ConsoleColor.Green);
                Debug.Log("加载传输类...", ConsoleColor.White, true);
                transport = assembly.CreateInstance(setting.Transport) as Transport;
                if (transport == null)
                {
                    Debug.Log("没有找到传输类!", ConsoleColor.Red);
                    Console.ReadKey();
                    Environment.Exit(0);
                    return;
                }

                var type = assembly.GetType(setting.Transport);
                Debug.Log("OK", ConsoleColor.Green);
                Debug.Log("加载传输方法...", ConsoleColor.White, true);
                if (type != null)
                {
                    awakeMethod = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    updateMethod = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                Debug.Log("OK", ConsoleColor.Green);
                awakeMethod?.Invoke(transport, null);
                Debug.Log("开始进行传输...", ConsoleColor.White, true);
                service = new Service(transport.GetMaxPacketSize());
                transport.OnServerConnected = clientId =>
                {
                    clients.Add(clientId);
                    service.ServerConnected(clientId);
                    Debug.Log($"客户端 {clientId} 连接到传输。", ConsoleColor.Cyan);
                    if (setting.UseNATPuncher)
                    {
                        var punchId = Guid.NewGuid().ToString();
                        punches.Add(clientId, punchId);
                        var position = 0;
                        buffers.WriteByte(ref position, (byte)OpCodes.NATPuncher);
                        buffers.WriteString(ref position, punchId);
                        buffers.WriteInt(ref position, setting.NATPunchPort);
                        transport.ServerSend(clientId, new ArraySegment<byte>(buffers, 0, position));
                    }
                };

                transport.OnServerReceive = service.ServerReceive;
                transport.OnServerDisconnected = clientId =>
                {
                    clients.Remove(clientId);
                    service.ServerDisconnected(clientId);
                    if (connections.ContainsKey(clientId))
                    {
                        connections.Remove(clientId);
                    }

                    if (punches.Keys.Contains(clientId))
                    {
                        punches.Remove(clientId);
                    }
                };

                transport.port = setting.EndPointPort;
                transport.StartServer();
                Debug.Log("OK", ConsoleColor.Green);

                if (setting.UseEndPoint)
                {
                    Debug.Log("开启REST服务...", ConsoleColor.White, true);
                    if (RestUtility.StartServer(setting.EndPointPort))
                    {
                        Debug.Log("OK", ConsoleColor.Green);
                    }
                    else
                    {
                        Debug.Log("请以管理员身份运行或检查端口是否被占用。", ConsoleColor.Red);
                    }
                }

                if (setting.UseNATPuncher)
                {
                    Debug.Log("开启NATP服务...", ConsoleColor.White, true);
                    try
                    {
                        punchClient = new UdpClient(setting.NATPunchPort);
                        Debug.Log("OK", ConsoleColor.Green);
                        Debug.Log("开启内网穿透线程...", ConsoleColor.White, true);
                        var thread = new Thread(NATPuncherThread);
                        try
                        {
                            thread.Start();
                        }
                        catch (Exception e)
                        {
                            Debug.Log(e.ToString(), ConsoleColor.Red);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log("请检查端口是否被占用...", ConsoleColor.Red);
                        Debug.Log(e.ToString(), ConsoleColor.Red);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString(), ConsoleColor.Red);
                Console.ReadKey();
                Environment.Exit(0);
            }

            while (true)
            {
                updateMethod?.Invoke(transport, null);
                heartBeat++;
                if (heartBeat >= setting.HeartBeat)
                {
                    heartBeat = 0;
                    foreach (var client in clients)
                    {
                        transport.ServerSend(client, new ArraySegment<byte>(new[] { byte.MaxValue }));
                    }

                    GC.Collect();
                }

                await Task.Delay(setting.UpdateTime);
            }
        }

        private void NATPuncherThread()
        {
            var endPoint = new IPEndPoint(IPAddress.Any, setting.NATPunchPort);
            Debug.Log("OK", ConsoleColor.Green);
            while (true)
            {
                var segment = punchClient.Receive(ref endPoint);
                var position = 0;
                try
                {
                    if (segment.ReadBool(ref position))
                    {
                        var clientId = segment.ReadString(ref position);
                        if (punches.TryGetSecond(clientId, out position))
                        {
                            Debug.Log($"客户端 {position} 建立内网穿透连接 " + endPoint);
                            connections.Add(position, new IPEndPoint(endPoint.Address, endPoint.Port));
                            punches.Remove(position);
                        }
                    }

                    punchClient.Send(new byte[] { 1 }, 1, endPoint);
                }
                catch (Exception e)
                {
                    Debug.Log(e.ToString(), ConsoleColor.Red);
                }
            }
        }
    }

    public static class Debug
    {
        public static void Log(string message, ConsoleColor color = ConsoleColor.White, bool nowrap = false)
        {
            Console.ForegroundColor = color;
            if (nowrap)
            {
                Console.Write(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
    }

    public static class RestUtility
    {
        public static bool StartServer(ushort port)
        {
            try
            {
                var builder = new ConfigurationBuilder();
                var config = builder.SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", true, true).Build();
                var server = new RestServerBuilder(new ServiceCollection(), config, ConfigureSerivces, ConfigureServer).Build();
                server.Router.Options.SendExceptionMessages = false;
                server.Start();
                return true;
            }
            catch
            {
                return false;
            }

            void ConfigureSerivces(IServiceCollection services)
            {
                services.AddLogging(builder => builder.AddConsole());
                services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.None);
            }

            void ConfigureServer(IRestServer server)
            {
                server.Prefixes.Add($"http://*:{port}/");
            }
        }
    }

    [RestResource]
    public class RestService
    {
        [RestRoute("Get", "/api/compressed/servers")]
        public async Task ServerListCompressed(IHttpContext context)
        {
            if (Program.setting.EndPointServerList)
            {
                var json = JsonConvert.SerializeObject(Program.instance.rooms);
                await context.Response.SendResponseAsync(json.Compress());
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }
    }
}