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

    public enum OpCodes : byte
    {
        Default = 0,
        RequestID = 1,
        JoinServer = 2,
        SendData = 3,
        GetID = 4,
        ServerJoined = 5,
        GetData = 6,
        CreateRoom = 7,
        ServerLeft = 8,
        PlayerDisconnected = 9,
        RoomCreated = 10,
        LeaveRoom = 11,
        KickPlayer = 12,
        AuthenticationRequest = 13,
        AuthenticationResponse = 14,
        Authenticated = 17,
        UpdateRoomData = 18,
        ServerConnectionData = 19,
        RequestNATConnection = 20,
        DirectConnectIP = 21
    }

    [Serializable]
    struct RelayStats
    {
        public int ConnectedClients;
        public int RoomCount;
        public int PublicRoomCount;
        public TimeSpan Uptime;
    }
}