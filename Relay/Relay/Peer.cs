using Grapevine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JFramework.Net
{
    [RestResource(BasePath = "/api/")]
    public sealed class Peer
    {
        private static readonly Dictionary<int, string> cachedServerListAppId = new Dictionary<int, string>();
        private static readonly Dictionary<int, string> compressedServerListAppId = new Dictionary<int, string>();
        private static string cachedServerList = "[]";
        private static string compressedServerList;
        public static DateTime lastPing = DateTime.Now;
        private static List<Room> rooms => Program.instance.GetRooms().Where(x => x.isPublic).ToList();
        private static List<List<Room>> appRooms => Program.instance.GetRooms().GroupBy(x => x.appId).Select(g => g.ToList()).ToList();

        private static RelayStats stats => new RelayStats()
        {
            ConnectedClients = Program.instance.GetConnections(),
            RoomCount = Program.instance.GetRooms().Count,
            PublicRoomCount = Program.instance.GetPublicRoomCount(),
            Uptime = Program.instance.GetUptime()
        };

        public static void RoomsModified()
        {
            cachedServerList = JsonConvert.SerializeObject(rooms, Formatting.Indented);
            compressedServerList = cachedServerList.Compress();

            cachedServerListAppId.Clear();
            compressedServerListAppId.Clear();

            foreach (var roomList in appRooms)
            {
                var jsonRooms = JsonConvert.SerializeObject(roomList, Formatting.Indented);
                cachedServerListAppId.Add(roomList.First().appId, jsonRooms);
                compressedServerListAppId.Add(roomList.First().appId, jsonRooms.Compress());
            }

            if (Program.setting.UseLoadBalancer)
            {
                Program.instance.UpdateLoadBalancerServers();
            }
        }

        [RestRoute("Get", "/stats")]
        public async Task Stats(IHttpContext context)
        {
            lastPing = DateTime.Now;
            string json = JsonConvert.SerializeObject(stats, Formatting.Indented);
            await context.Response.SendResponseAsync(json);
        }

        [RestRoute("Get", "/servers")]
        public async Task ServerList(IHttpContext context)
        {
            if (Program.setting.EndpointServerList)
            {
                await context.Response.SendResponseAsync(cachedServerList);
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }

        [RestRoute("Get", "/servers/{appId:num}")]
        public async Task ServerListAppId(IHttpContext context)
        {
            if (Program.setting.EndpointServerList)
            {
                int appId = int.Parse(context.Request.PathParameters["appId"]);
                await context.Response.SendResponseAsync(cachedServerListAppId.GetValueOrDefault(appId, "[]"));
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }

        [RestRoute("Get", "/compressed/servers")]
        public async Task ServerListCompressed(IHttpContext context)
        {
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");

            if (Program.setting.EndpointServerList)
            {
                await context.Response.SendResponseAsync(compressedServerList);
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }

        [RestRoute("Get", "/compressed/servers/{appId:num}")]
        public async Task ServerListCompressedAppId(IHttpContext context)
        {
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");

            if (Program.setting.EndpointServerList)
            {
                int appId = int.Parse(context.Request.PathParameters["appId"]);
                await context.Response.SendResponseAsync(compressedServerListAppId.GetValueOrDefault(appId, "[]".Compress()));
            }
            else
            {
                await context.Response.SendResponseAsync(HttpStatusCode.Forbidden);
            }
        }

        [RestRoute("Options", "/compressed/servers")]
        public async Task ServerListCompressedOptions(IHttpContext context)
        {
            var originHeaders = context.Request.Headers["Access-Control-Request-Headers"];

            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
            context.Response.Headers.Add("Access-Control-Allow-Headers", originHeaders);

            await context.Response.SendResponseAsync(HttpStatusCode.Ok);
        }
    }
}