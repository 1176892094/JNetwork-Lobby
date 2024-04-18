using System;
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

namespace JFramework.Net
{
    public partial class Program
    {
        public static string address;
        public static Setting setting;
        public static Program instance;
        public static Transport transport;
        public static readonly WebClient webClient = new WebClient();
        public readonly Dictionary<int, IPEndPoint> NATConnections = new Dictionary<int, IPEndPoint>();

        private DateTime startUpTime;
        private MethodInfo awakeMethod;
        private MethodInfo startMethod;
        private MethodInfo updateMethod;
        private MethodInfo lateUpdateMethod;
        private RelayHandler relayHandler;

        private int punchPosition;
        private int heartBeatTimer;
        private UdpClient punchServer;
        private readonly byte[] punchRequest = new byte[500];
        private readonly List<int> connections = new List<int>();
        private readonly HashMap<int, string> pendingPunches = new HashMap<int, string>();
    }

    public partial class Program
    {
        public static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
        {
            instance = this;
            startUpTime = DateTime.Now;
            WriteLogMessage("启动中继服务器!", ConsoleColor.Green);

            try
            {
                address = webClient.DownloadString("https://api.ipify.org/").Replace("\\r", "").Replace("\\n", "").Trim();
                WriteLogMessage($"当前地址：{address}", ConsoleColor.Cyan);
            }
            catch
            {
                WriteLogMessage("无法到达公网IP地址，使用环回地址。", ConsoleColor.Yellow);
                address = "127.0.0.1";
            }

            var isActive = bool.Parse(Environment.GetEnvironmentVariable("NO_SETTING") ?? "false");
            var settingPath = Environment.GetEnvironmentVariable("SETTING_PATH") ?? "setting.json";
            if (!File.Exists(settingPath) && !isActive)
            {
                WriteLogMessage("请将 setting.json 文件配置正确并重新运行!", ConsoleColor.Yellow);
                await File.WriteAllTextAsync(settingPath, JsonConvert.SerializeObject(new Setting(), Formatting.Indented));
                Console.ReadKey();
                Environment.Exit(0);
            }
            else
            {
                if (!isActive)
                {
                    setting = JsonConvert.DeserializeObject<Setting>(await File.ReadAllTextAsync(settingPath));
                    if (ushort.TryParse(Environment.GetEnvironmentVariable("RELAY_ENDPOINT_PORT"), out ushort relayPort))
                    {
                        setting.RelayPort = relayPort; // 中继服务器端口
                    }

                    if (ushort.TryParse(Environment.GetEnvironmentVariable("RELAY_TRANSPORT_PORT"), out ushort transportPort))
                    {
                        setting.TransportPort = transportPort; // 传输端口
                    }

                    if (ushort.TryParse(Environment.GetEnvironmentVariable("RELAY_PUNCHER_PORT"), out ushort puncherPort))
                    {
                        setting.PuncherPort = puncherPort; // 内网穿透端口
                    }

                    var authKey = Environment.GetEnvironmentVariable("RELAY_LB_AUTHKEY");
                    if (!string.IsNullOrWhiteSpace(authKey))
                    {
                        setting.LoadBalancerAuthKey = authKey;
                        WriteLogMessage("从环境变量加载负载均衡器认证密钥。", ConsoleColor.Green);
                    }
                }
                else
                {
                    setting = new Setting
                    {
                        TransportClass = Environment.GetEnvironmentVariable("TRANSPORT_CLASS") ?? "JFramework.Net.NetworkTransport",
                        AuthenticationKey = Environment.GetEnvironmentVariable("AUTH_KEY") ?? "Secret Auth Key",
                        TransportPort = ushort.Parse(Environment.GetEnvironmentVariable("TRANSPORT_PORT") ?? "9987"),
                        UpdateLoopTime = int.Parse(Environment.GetEnvironmentVariable("UPDATE_LOOP_TIME") ?? "10"),
                        HeartBeatInterval = int.Parse(Environment.GetEnvironmentVariable("UPDATE_HEARTBEAT_INTERVAL") ?? "100"),
                        RandomIdLength = int.Parse(Environment.GetEnvironmentVariable("RANDOMLY_GENERATED_ID_LENGTH") ?? "5"),
                        UseEndpoint = bool.Parse(Environment.GetEnvironmentVariable("USE_ENDPOINT") ?? "true"),
                        RelayPort = ushort.Parse(Environment.GetEnvironmentVariable("ENDPOINT_PORT") ?? "8080"),
                        EndpointServerList = bool.Parse(Environment.GetEnvironmentVariable("ENDPOINT_SERVERLIST") ?? "true"),
                        EnablePunchServer = bool.Parse(Environment.GetEnvironmentVariable("ENABLE_NATPUNCH_SERVER") ?? "true"),
                        PuncherPort = ushort.Parse(Environment.GetEnvironmentVariable("NAT_PUNCH_PORT") ?? "7776"),
                        UseLoadBalancer = bool.Parse(Environment.GetEnvironmentVariable("USE_LOAD_BALANCER") ?? "false"),
                        LoadBalancerAuthKey = Environment.GetEnvironmentVariable("LOAD_BALANCER_AUTH_KEY") ?? "AuthKey",
                        LoadBalancerAddress = Environment.GetEnvironmentVariable("LOAD_BALANCER_ADDRESS") ?? "127.0.0.1",
                        LoadBalancerPort = ushort.Parse(Environment.GetEnvironmentVariable("LOAD_BALANCER_PORT") ?? "7070"),
                        LoadBalancerRegion = (Regions)int.Parse(Environment.GetEnvironmentVariable("LOAD_BALANCER_REGION") ?? "1")
                    };
                }

                WriteLogMessage("加载程序集...");
                try
                {
                    var assembly = Assembly.LoadFile(Path.GetFullPath(Setting.Assembly));
                    WriteLogMessage("OK", ConsoleColor.Green);
                    WriteLogMessage("加载传输类...");
                    transport = assembly.CreateInstance(setting.TransportClass) as Transport;
                    if (transport != null)
                    {
                        ConfigureTransport(assembly);

                        if (setting.UseEndpoint)
                        {
                            ConfigureEndPoint();
                        }

                        if (setting.EnablePunchServer)
                        {
                            ConfigurePunch();
                        }
                    }
                    else
                    {
                        WriteLogMessage("没有找到传输类...!", ConsoleColor.Red);
                        Console.ReadKey();
                        Environment.Exit(0);
                    }
                }
                catch (Exception e)
                {
                    WriteLogMessage("加载传输类异常：\n" + e, ConsoleColor.Red);
                    Console.ReadKey();
                    Environment.Exit(0);
                }

                if (setting.UseLoadBalancer)
                {
                    await RegisterSelfToLoadBalancer();
                }
            }

            await HeartbeatLoop();
        }

        private async Task HeartbeatLoop()
        {
            byte[] heartbeat = { 200 };
            while (true)
            {
                try
                {
                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(transport, null);
                    }

                    if (lateUpdateMethod != null)
                    {
                        lateUpdateMethod.Invoke(transport, null);
                    }
                }
                catch (Exception e)
                {
                    WriteLogMessage("传输期间异常：\n" + e, ConsoleColor.Red);
                }

                heartBeatTimer++;

                if (heartBeatTimer >= setting.HeartBeatInterval)
                {
                    heartBeatTimer = 0;

                    foreach (var connectionId in connections)
                    {
                        transport.ServerSend(connectionId, new ArraySegment<byte>(heartbeat));
                    }

                    if (setting.UseLoadBalancer)
                    {
                        if (DateTime.Now > Peer.lastPing.AddSeconds(60))
                        {
                            RegisterSelfToLoadBalancer();
                        }
                    }

                    GC.Collect();
                }

                await Task.Delay(setting.UpdateLoopTime);
            }
        }

        public async void UpdateLoadBalancerServers()
        {
            try
            {
                using var client = new WebClient();
                client.Headers.Add("Authorization", setting.LoadBalancerAuthKey);
                await client.DownloadStringTaskAsync($"http://{setting.LoadBalancerAddress}:{setting.LoadBalancerPort}/api/roomsupdated");
            }
            catch
            {
                // ignored
            }
        }

        private async Task RegisterSelfToLoadBalancer()
        {
            Peer.lastPing = DateTime.Now;
            try
            {
                if (setting.LoadBalancerAddress.ToLower() == "localhost")
                {
                    setting.LoadBalancerAddress = "127.0.0.1";
                }

                var uri = new Uri($"http://{setting.LoadBalancerAddress}:{setting.LoadBalancerPort}/api/auth");
                var endpointPort = setting.RelayPort.ToString();
                var gamePort = setting.TransportPort.ToString();
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.Headers.Add("Authorization", setting.LoadBalancerAuthKey);
                request.Headers.Add("x-EndpointPort", endpointPort);
                request.Headers.Add("x-GamePort", gamePort);
                request.Headers.Add("x-PIP", address);
                request.Headers.Add("x-Region", ((int)setting.LoadBalancerRegion).ToString());
                await request.GetResponseAsync();
            }
            catch
            {
                WriteLogMessage("添加错误或负载均衡器不可用", ConsoleColor.Red);
            }
        }
    }

    public partial class Program
    {
        private void ConfigureTransport(Assembly assembly)
        {
            var type = assembly.GetType(setting.TransportClass);
            WriteLogMessage("OK", ConsoleColor.Green);
            WriteLogMessage("加载传输方法... ");
            if (type != null)
            {
                awakeMethod = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                startMethod = type.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updateMethod = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                lateUpdateMethod = type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            WriteLogMessage("OK", ConsoleColor.Green);
            awakeMethod?.Invoke(transport, null);
            startMethod?.Invoke(transport, null);
            WriteLogMessage("开始进行传输... ");

            transport.OnServerConnected = clientId =>
            {
                WriteLogMessage($"客户端 {clientId} 连接到传输。", ConsoleColor.Cyan);
                connections.Add(clientId);
                relayHandler.ClientConnected(clientId);

                if (setting.EnablePunchServer)
                {
                    var punchId = Guid.NewGuid().ToString();
                    pendingPunches.Add(clientId, punchId);
                    punchPosition = 0;
                    punchRequest.Write(ref punchPosition, (byte)OpCode.RequestNATConnection);
                    punchRequest.Write(ref punchPosition, punchId);
                    punchRequest.Write(ref punchPosition, setting.PuncherPort);
                    transport.ServerSend(clientId, new ArraySegment<byte>(punchRequest, 0, punchPosition));
                }
            };

            relayHandler = new RelayHandler(transport.GetMaxPacketSize());

            transport.OnServerReceive = relayHandler.HandleMessage;
            transport.OnServerDisconnected = clientId =>
            {
                WriteLogMessage($"客户端 {clientId} 断开连接...", ConsoleColor.Cyan);
                connections.Remove(clientId);
                relayHandler.HandleDisconnect(clientId);

                if (NATConnections.ContainsKey(clientId))
                {
                    NATConnections.Remove(clientId);
                }

                if (pendingPunches.TryGetFirst(clientId, out _))
                {
                    pendingPunches.Remove(clientId);
                }
            };

            transport.port = setting.TransportPort;
            transport.StartServer();
            WriteLogMessage("OK", ConsoleColor.Green);
        }

        private static void ConfigureEndPoint()
        {
            WriteLogMessage("开启端口服务... ");

            if (Start(setting.RelayPort))
            {
                WriteLogMessage("OK", ConsoleColor.Green);
                Peer.RoomsModified();
            }
            else
            {
                WriteLogMessage("请以管理员身份运行或检查端口是否被占用。", ConsoleColor.Red);
            }

            bool Start(ushort port = 8080, bool ssl = false)
            {
                try
                {
                    var builder = new ConfigurationBuilder();
                    var config = builder.SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", true, false).Build();
                    var server = new RestServerBuilder(new ServiceCollection(), config, services =>
                    {
                        services.AddLogging(configure => configure.AddConsole());
                        services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.None);
                    }, server => { server.Prefixes.Add(ssl ? $"https://*:{port}/" : $"http://*:{port}/"); }).Build();
                    server.Router.Options.SendExceptionMessages = false;
                    server.Start();
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }
        }

        private void ConfigurePunch()
        {
            WriteLogMessage("开启内网穿透...");

            try
            {
                punchServer = new UdpClient(setting.PuncherPort);
                WriteLogMessage("OK", ConsoleColor.Green);
                WriteLogMessage("开启内网穿透线程... ");
                var thread = new Thread(PunchLoopThread);
                try
                {
                    thread.Start();
                }
                catch (Exception e)
                {
                    WriteLogMessage(e.ToString(), ConsoleColor.Red);
                }
            }
            catch (Exception e)
            {
                WriteLogMessage("请检查端口是否被占用...", ConsoleColor.Red);
                WriteLogMessage(e.ToString(), ConsoleColor.Red);
            }
        }

        private void PunchLoopThread()
        {
            WriteLogMessage("OK", ConsoleColor.Green);
            IPEndPoint endpoint = new(IPAddress.Any, setting.PuncherPort);
            var response = new byte[] { 1 };
            while (true)
            {
                var readData = punchServer.Receive(ref endpoint);
                var position = 0;
                try
                {
                    readData.Read(ref position, out bool isConnectionEstablished);

                    if (isConnectionEstablished)
                    {
                        readData.Read(ref position, out string clientId);

                        if (pendingPunches.TryGetSecond(clientId, out position))
                        {
                            NATConnections.Add(position, new IPEndPoint(endpoint.Address, endpoint.Port));
                            pendingPunches.Remove(position);
                            WriteLogMessage("客户端成功建立内网穿透连接。" + endpoint);
                        }
                    }

                    punchServer.Send(response, 1, endpoint);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    public partial class Program
    {
        public List<Room> GetRooms() => relayHandler.rooms;
        public int GetConnections() => connections.Count;
        public TimeSpan GetUptime() => DateTime.Now - startUpTime;
        public int GetPublicRoomCount() => relayHandler.rooms.Count(x => x.isPublic);

        public static void WriteLogMessage(string message, ConsoleColor color = ConsoleColor.White, bool oneLine = false)
        {
            Console.ForegroundColor = color;
            if (oneLine)
            {
                Console.Write(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
    }
}