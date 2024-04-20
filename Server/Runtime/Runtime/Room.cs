using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;

namespace JFramework.Net
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Room
    {
        public int serverId;
        public int hostId;
        public string serverName;
        public string serverData;
        public bool isPublic;
        public int maxPlayers;
        public List<int> clients;
        [JsonIgnore] public bool supportsDirectConnect;
        [JsonIgnore] public IPEndPoint hostIP;
        [JsonIgnore] public string hostLocalIP;
        [JsonIgnore] public bool useNATPunch;
        [JsonIgnore] public int port;
    }

    public enum Channel : sbyte
    {
        Reliable = 1,
        Unreliable = 2
    }

    public enum OpCodes
    {
        Default = 0,
        RequestId = 1,
        ResponseId = 2,
        JoinServer = 3,
        JoinServerAfter = 4,
        CreateRoom = 5,
        CreateRoomAfter = 6,
        UpdateRoom = 7,
        LeaveRoom = 8,
        SendData = 9,
        ReceiveData = 10,
        LeaveServer = 11,
        Disconnect = 12,
        RemoveClient = 13,
        Authority = 14,
        AuthorityRequest = 15,
        AuthorityResponse = 16,
        ServerConnectionData = 17,
        NATRequest = 18,
        NATAddress = 19
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