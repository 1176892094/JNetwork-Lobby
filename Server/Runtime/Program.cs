using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace JFramework.Net
{
    internal class Program
    {
        public static Setting setting;
        public static Program instance;
        public static Transport transport;
        public readonly Dictionary<int, IPEndPoint> connections = new Dictionary<int, IPEndPoint>();

        private int heartBeat;
        private UdpClient punchClient;
        private MethodInfo awakeMethod;
        private MethodInfo updateMethod;
        private RelayHelper relayHepler;
        private readonly byte[] buffers = new byte[500];
        private readonly List<int> clients = new List<int>();
        private readonly HashMap<int, string> punches = new HashMap<int, string>();

        private const string SETTING = "setting.json";
        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();
        public List<Room> rooms => relayHepler.rooms.Values.ToList();

        public async Task MainAsync()
        {
            instance = this;
            WriteLogMessage("启动中继服务器!", ConsoleColor.Green);

            if (!File.Exists(SETTING))
            {
                await File.WriteAllTextAsync(SETTING, JsonConvert.SerializeObject(new Setting(), Formatting.Indented));
                WriteLogMessage("请将 setting.json 文件配置正确并重新运行!", ConsoleColor.Yellow);
                Console.ReadKey();
                Environment.Exit(0);
            }
            else
            {
                setting = JsonConvert.DeserializeObject<Setting>(await File.ReadAllTextAsync(SETTING));
                WriteLogMessage("加载程序集...", ConsoleColor.White, true);
                try
                {
                    var assembly = Assembly.LoadFile(Path.GetFullPath(setting.Assembly));
                    WriteLogMessage("OK", ConsoleColor.Green);
                    WriteLogMessage("加载传输类...", ConsoleColor.White, true);
                    transport = assembly.CreateInstance(setting.Transport) as Transport;
                    if (transport != null)
                    {
                        var type = assembly.GetType(setting.Transport);
                        WriteLogMessage("OK", ConsoleColor.Green);
                        WriteLogMessage("加载传输方法...", ConsoleColor.White, true);
                        if (type != null)
                        {
                            awakeMethod = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            updateMethod = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        }

                        WriteLogMessage("OK", ConsoleColor.Green);
                        awakeMethod?.Invoke(transport, null);
                        WriteLogMessage("开始进行传输...", ConsoleColor.White, true);

                        transport.OnServerConnected = clientId =>
                        {
                            WriteLogMessage($"客户端 {clientId} 连接到传输。", ConsoleColor.Cyan);
                            clients.Add(clientId);
                            relayHepler.ServerConnected(clientId);
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

                        relayHepler = new RelayHelper(transport.GetMaxPacketSize(0));
                        transport.OnServerReceive = relayHepler.ServerReceive;
                        transport.OnServerDisconnected = clientId =>
                        {
                            clients.Remove(clientId);
                            relayHepler.ServerDisconnected(clientId);
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
                        WriteLogMessage("OK", ConsoleColor.Green);

                        if (setting.UseEndPoint)
                        {
                            WriteLogMessage("开启端口服务...", ConsoleColor.White, true);
                            if (new RelayProxyServer().StartServer(setting.EndPointPort))
                            {
                                WriteLogMessage("OK", ConsoleColor.Green);
                            }
                            else
                            {
                                WriteLogMessage("请以管理员身份运行或检查端口是否被占用。", ConsoleColor.Red);
                            }
                        }

                        if (setting.UseNATPuncher)
                        {
                            WriteLogMessage("开启内网穿透...", ConsoleColor.White, true);
                            try
                            {
                                punchClient = new UdpClient(setting.NATPunchPort);
                                WriteLogMessage("OK", ConsoleColor.Green);
                                WriteLogMessage("开启内网穿透线程...", ConsoleColor.White, true);
                                var thread = new Thread(NATPuncherThread);
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
                    }
                    else
                    {
                        WriteLogMessage("没有找到传输类!", ConsoleColor.Red);
                        Console.ReadKey();
                        Environment.Exit(0);
                    }
                }
                catch (Exception e)
                {
                    WriteLogMessage(e.ToString(), ConsoleColor.Red);
                    Console.ReadKey();
                    Environment.Exit(0);
                }
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
            WriteLogMessage("OK", ConsoleColor.Green);
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
                            WriteLogMessage($"客户端 {position} 建立内网穿透连接 " + endPoint);
                            connections.Add(position, new IPEndPoint(endPoint.Address, endPoint.Port));
                            punches.Remove(position);
                        }
                    }

                    punchClient.Send(new byte[] { 1 }, 1, endPoint);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        private static void WriteLogMessage(string message, ConsoleColor color = ConsoleColor.White, bool isLine = false)
        {
            Console.ForegroundColor = color;
            if (isLine)
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