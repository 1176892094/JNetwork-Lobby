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

        private const string SETTING = "setting.json";
        public List<Room> rooms => process.rooms.Values.ToList();

        public static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

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

            try
            {
                Setting = JsonConvert.DeserializeObject<Setting>(await File.ReadAllTextAsync(SETTING));

                Debug.Log("加载程序集...");
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

                Debug.Log("加载传输方法...");
                var type = assembly.GetType(Setting.Transport);
                awakeMethod = type?.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                updateMethod = type?.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                Debug.Log("开始进行传输...");
                awakeMethod?.Invoke(transport, null);
                process = new Process(transport);

                transport.OnServerConnect = process.ServerConnected;
                transport.OnServerDisconnect = process.ServerDisconnected;
                transport.OnServerReceive = process.ServerReceive;

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
                heartBeat++;
                if (heartBeat >= Setting.HeartBeat)
                {
                    heartBeat = 0;
                    GC.Collect();
                }

                updateMethod?.Invoke(transport, null);
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

        public static string Compress(string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }

            return Convert.ToBase64String(output.ToArray());
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