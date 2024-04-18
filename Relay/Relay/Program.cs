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
        public readonly Dictionary<int, IPEndPoint> connections = new Dictionary<int, IPEndPoint>();
        public static Setting setting;
        public static Program instance;
        public static Transport transport;

        private int heartBeat;
        private DateTime startTime;
        private RelayEvent relay;
        private MethodInfo awakeMethod;
        private MethodInfo startMethod;
        private MethodInfo updateMethod;
        private MethodInfo lateMethod;
        private UdpClient punchServer;
        private int NATRequestPosition;
        private readonly byte[] NATRequest = new byte[500];
        private readonly List<int> clients = new List<int>();
        private readonly HashMap<int, string> punches = new HashMap<int, string>();

        private const string SETTING = "setting.json";

        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

        public int Count() => clients.Count;
        public TimeSpan SinceTime() => DateTime.Now - startTime;
        public int GetPublicRoomCount() => relay.rooms.Count(x => x.isPublic);
        public List<Room> GetRooms() => relay.rooms;

        public async Task MainAsync()
        {
            instance = this;
            startTime = DateTime.Now;
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
                    transport = assembly.CreateInstance(setting.TransportClass) as Transport;
                    if (transport != null)
                    {
                        var type = assembly.GetType(setting.TransportClass);
                        WriteLogMessage("OK", ConsoleColor.Green);
                        WriteLogMessage("加载传输方法...", ConsoleColor.White, true);
                        if (type != null)
                        {
                            awakeMethod = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            startMethod = type.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            updateMethod = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            lateMethod = type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        }

                        WriteLogMessage("OK", ConsoleColor.Green);
                        awakeMethod?.Invoke(transport, null);
                        startMethod?.Invoke(transport, null);
                        WriteLogMessage("开始进行传输...", ConsoleColor.White, true);

                        transport.OnServerConnected = clientId =>
                        {
                            WriteLogMessage($"客户端 {clientId} 连接到传输。", ConsoleColor.Cyan);
                            clients.Add(clientId);
                            relay.ServerConnected(clientId);
                            if (setting.EnableNATPunchServer)
                            {
                                var punchId = Guid.NewGuid().ToString();
                                punches.Add(clientId, punchId);
                                NATRequestPosition = 0;
                                NATRequest.WriteByte(ref NATRequestPosition, (byte)OpCodes.RequestNATConnection);
                                NATRequest.WriteString(ref NATRequestPosition, punchId);
                                transport.ServerSend(clientId, new ArraySegment<byte>(NATRequest, 0, NATRequestPosition));
                            }
                        };

                        relay = new RelayEvent(transport.GetMaxPacketSize(0));

                        transport.OnServerReceive = relay.ServerReceive;
                        transport.OnServerDisconnected = clientId =>
                        {
                            clients.Remove(clientId);
                            relay.ServerDisconnected(clientId);

                            if (connections.ContainsKey(clientId))
                            {
                                connections.Remove(clientId);
                            }

                            if (punches.TryGetFirst(clientId, out _))
                            {
                                punches.Remove(clientId);
                            }
                        };

                        transport.StartServer();

                        WriteLogMessage("OK", ConsoleColor.Green);
                        if (setting.UseEndPoint)
                        {
                            WriteLogMessage("开启端口服务...", ConsoleColor.White, true);
                            var endpoint = new RelayServer();
                            if (endpoint.Start(setting.EndpointPort))
                            {
                                WriteLogMessage("OK", ConsoleColor.Green);
                            }
                            else
                            {
                                WriteLogMessage("请以管理员身份运行或检查端口是否被占用。", ConsoleColor.Red);
                            }
                        }

                        if (setting.EnableNATPunchServer)
                        {
                            WriteLogMessage("开启内网穿透...", ConsoleColor.White, true);
                            try
                            {
                                punchServer = new UdpClient(setting.NATPunchPort);
                                WriteLogMessage("OK", ConsoleColor.Green);
                                WriteLogMessage("开启内网穿透线程...", ConsoleColor.White, true);
                                var thread = new Thread(PunchThread);
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
                lateMethod?.Invoke(transport, null);
                heartBeat++;

                if (heartBeat >= setting.UpdateHeartbeatInterval)
                {
                    heartBeat = 0;
                    foreach (var client in clients)
                    {
                        transport.ServerSend(client, new ArraySegment<byte>(new byte[] { 200 }));
                    }

                    GC.Collect();
                }

                await Task.Delay(setting.UpdateLoopTime);
            }
        }

        private void PunchThread()
        {
            WriteLogMessage("OK", ConsoleColor.Green);
            var endPoint = new IPEndPoint(IPAddress.Any, setting.NATPunchPort);
            var serverResponse = new byte[] { 1 };

            while (true)
            {
                var readData = punchServer.Receive(ref endPoint);
                var position = 0;
                try
                {
                    if (readData.ReadBool(ref position))
                    {
                        var clientId = readData.ReadString(ref position);
                        if (punches.TryGetSecond(clientId, out position))
                        {
                            connections.Add(position, new IPEndPoint(endPoint.Address, endPoint.Port));
                            punches.Remove(position);
                            WriteLogMessage("客户端成功建立内网穿透连接。" + endPoint);
                        }
                    }

                    punchServer.Send(serverResponse, 1, endPoint);
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static void WriteLogMessage(string message, ConsoleColor color = ConsoleColor.White, bool oneLine = false)
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