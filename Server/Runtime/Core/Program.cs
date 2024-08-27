using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
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
        public static Program Instance;
        public static Setting Setting;
        
        private int heartBeat;
        private Process process;
        private Transport transport;
        private MethodInfo awakeMethod;
        private MethodInfo updateMethod;
        private readonly HashSet<int> clients = new HashSet<int>();

        private const string SETTING = "setting.json";
        public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();
        public List<Room> rooms => process.rooms.Values.ToList();

        public async Task MainAsync()
        {
            Instance = this;
            Debug.Log($"启动中继服务器!");
            if (!File.Exists(SETTING))
            {
                await File.WriteAllTextAsync(SETTING, JsonConvert.SerializeObject(new Setting(), Formatting.Indented));
                Debug.Log("请将 setting.json 文件配置正确并重新运行!", ConsoleColor.Yellow);
                Console.ReadKey();
                Environment.Exit(0);
                return;
            }

            Setting = JsonConvert.DeserializeObject<Setting>(await File.ReadAllTextAsync(SETTING));
            Debug.Log("加载程序集...");
            try
            {
                var assembly = Assembly.LoadFile(Path.GetFullPath(Setting.Assembly));
                Debug.Log("加载传输类...");
                transport = assembly.CreateInstance(Setting.Transport) as Transport;
                if (transport == null)
                {
                    Debug.Log("没有找到传输类!", ConsoleColor.Red);
                    Console.ReadKey();
                    Environment.Exit(0);
                    return;
                }

                var type = assembly.GetType(Setting.Transport);
                Debug.Log("加载传输方法...");
                if (type != null)
                {
                    awakeMethod = type.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    updateMethod = type.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                awakeMethod?.Invoke(transport, null);
                Debug.Log("开始进行传输...");
                process = new Process(transport, Setting.ServerKey);
                transport.OnServerConnect = clientId =>
                {
                    clients.Add(clientId);
                    process.ServerConnected(clientId);
                };

                transport.OnServerReceive = process.ServerReceive;
                transport.OnServerDisconnect = clientId =>
                {
                    clients.Remove(clientId);
                    process.ServerDisconnected(clientId);
                };

                transport.port = Setting.EndPointPort;
                transport.StartServer();

                if (Setting.UseEndPoint)
                {
                    Debug.Log("开启REST服务...");
                    if (!RestUtility.StartServer(Setting.EndPointPort))
                    {
                        Debug.Log("请以管理员身份运行或检查端口是否被占用。", ConsoleColor.Red);
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
                if (heartBeat >= Setting.HeartBeat)
                {
                    heartBeat = 0;
                    foreach (var client in clients)
                    {
                        transport.SendToClient(client, new ArraySegment<byte>(new[] { byte.MaxValue }));
                    }

                    GC.Collect();
                }

                await Task.Delay(Setting.UpdateTime);
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
                var server = new RestServerBuilder(new ServiceCollection(), config, ConfigureServices, ConfigureServer).Build();
                server.Router.Options.SendExceptionMessages = false;
                server.Start();
                return true;
            }
            catch
            {
                return false;
            }

            void ConfigureServices(IServiceCollection services)
            {
                services.AddLogging(builder => builder.AddConsole());
                services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.None);
            }

            void ConfigureServer(IRestServer server)
            {
                server.Prefixes.Add($"http://*:{port}/");
            }
        }

        public static string Compress(string text)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            var memoryStream = new MemoryStream();
            using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
            {
                gZipStream.Write(buffer, 0, buffer.Length);
            }

            memoryStream.Position = 0;

            var compressedData = new byte[memoryStream.Length];
            memoryStream.Read(compressedData, 0, compressedData.Length);

            var gZipBuffer = new byte[compressedData.Length + 4];
            Buffer.BlockCopy(compressedData, 0, gZipBuffer, 4, compressedData.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(buffer.Length), 0, gZipBuffer, 0, 4);
            return Convert.ToBase64String(gZipBuffer);
        }

        public static string Decompress(string compressedText)
        {
            byte[] gZipBuffer = Convert.FromBase64String(compressedText);
            using (var memoryStream = new MemoryStream())
            {
                int dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                memoryStream.Write(gZipBuffer, 4, gZipBuffer.Length - 4);

                var buffer = new byte[dataLength];

                memoryStream.Position = 0;
                using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                {
                    gZipStream.Read(buffer, 0, buffer.Length);
                }

                return Encoding.UTF8.GetString(buffer);
            }
        }
    }

    public static class Debug
    {
        public static void Log(string message, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:MM-dd HH:mm:ss}] " + message);
        }
    }

    [RestResource]
    public class RestService
    {
        [RestRoute("Get", "/api/compressed/servers")]
        public async Task ServerListCompressed(IHttpContext context)
        {
            if (Program.Setting.EndPointServerList)
            {
                var json = JsonConvert.SerializeObject(Program.Instance.rooms);
                await context.Response.SendResponseAsync(RestUtility.Compress(json));
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }
    }
}