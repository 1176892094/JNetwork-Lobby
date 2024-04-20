using System;
using Grapevine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace JFramework.Net
{
    [RestResource]
    public class RelayProxy
    {
        private List<Room> rooms => Program.instance.GetRooms();

        private RelayStats stats => new RelayStats
        {
            ConnectedClients = Program.instance.Count(),
            RoomCount = Program.instance.GetRooms().Count,
            PublicRoomCount = Program.instance.GetPublicRoomCount(),
            Uptime = Program.instance.SinceTime()
        };

        [RestRoute("Get", "/api/stats")]
        public async Task Stats(IHttpContext context)
        {
            string json = JsonConvert.SerializeObject(stats, Formatting.Indented);
            await context.Response.SendResponseAsync(json);
        }

        [RestRoute("Get", "/api/servers")]
        public async Task ServerList(IHttpContext context)
        {
            if (Program.setting.EndPointServerList)
            {
                string json = JsonConvert.SerializeObject(rooms, Formatting.Indented);
                await context.Response.SendResponseAsync(json);
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }

        [RestRoute("Get", "/api/compressed/servers")]
        public async Task ServerListCompressed(IHttpContext context)
        {
            if (Program.setting.EndPointServerList)
            {
                string json = JsonConvert.SerializeObject(rooms);
                await context.Response.SendResponseAsync(json.Compress());
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }
    }

    public class RelayProxyServer
    {
        public bool Start(ushort port = 8080)
        {
            try
            {
                var builder = new ConfigurationBuilder();
                var config = builder.SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", true, true).Build();
                var server = new RestServerBuilder(new ServiceCollection(), config, services =>
                {
                    services.AddLogging(configure => configure.AddConsole());
                    services.Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.None);
                }, server => server.Prefixes.Add($"http://*:{port}/")).Build();

                server.Router.Options.SendExceptionMessages = false;
                server.Start();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}