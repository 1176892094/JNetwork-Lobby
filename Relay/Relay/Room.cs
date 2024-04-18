using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;

namespace JFramework.Net
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Room
    {
        public string serverId;
        public int hostId;
        public string serverName;
        public string serverData;
        public bool isPublic;
        public int maxPlayers;

        public int appId;
        public string version;

        public RelayAddress relayInfo;

        [JsonIgnore] public bool supportsDirectConnect;
        [JsonIgnore] public IPEndPoint hostIp;
        [JsonIgnore] public string hostLocalIp;
        [JsonIgnore] public bool useNATPunch;
        [JsonIgnore] public int port;
        [JsonIgnore] public List<int> clients;
        public int currentPlayers => clients.Count + 1;
    }

    [Serializable]
    public struct RelayAddress
    {
        public ushort port;
        public ushort endpointPort;
        public string address;
        public Regions serverRegion;
    }

    [Serializable]
    internal struct RelayStats
    {
        public int ConnectedClients;
        public int RoomCount;
        public int PublicRoomCount;
        public TimeSpan Uptime;
    }
}