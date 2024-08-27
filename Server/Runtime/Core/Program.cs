using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

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

        public List<Room> rooms => process.rooms.Values.ToList();

        public static void Main(string[] args)
        {
            new Program().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
        {
            try
            {
                Instance = this;
                Debug.Log("启动中继服务器!");
                if (!File.Exists("setting.json"))
                {
                    Debug.LogWarning("请将 setting.json 文件配置正确并重新运行。");
                    await File.WriteAllTextAsync("setting.json", JsonConvert.SerializeObject(new Setting(), Formatting.Indented));
                    Console.ReadKey();
                    Environment.Exit(0);
                    return;
                }

                Setting = JsonConvert.DeserializeObject<Setting>(await File.ReadAllTextAsync("setting.json"));

                Debug.Log("加载程序集...");
                var assembly = Assembly.LoadFile(Path.GetFullPath(Setting.Assembly));

                Debug.Log("加载传输类...");
                transport = assembly.CreateInstance(Setting.Transport) as Transport;
                if (transport == null)
                {
                    Debug.LogError("没有找到传输类!");
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
                        Debug.LogError("请以管理员身份运行或检查端口是否被占用。");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e.ToString());
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
}