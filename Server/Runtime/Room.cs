using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;

namespace JFramework.Net
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Room
    {
        public int clientId;
        public string serverId;
        public string serverName;
        public string serverData;
        public bool isPublic;
        public int maxPlayers;
        public List<int> clients;
        [JsonIgnore] public int port;
        [JsonIgnore] public string address;
        [JsonIgnore] public bool isPunch;
        [JsonIgnore] public bool isDirect;
        [JsonIgnore] public IPEndPoint proxy;
    }

    public enum Channel : sbyte
    {
        Reliable = 1,
        Unreliable = 2
    }

    public enum OpCodes
    {
        JoinRoom = 1,
        CreateRoom = 2,
        UpdateRoom = 3,
        LeaveRoom = 4,
        SendData = 5,
        ReceiveData = 6,
        Disconnect = 7,
        RemoveClient = 8,
        OnClientAuthority = 9,
        OnClientConnect = 10,
        OnServerAuthority = 11,
        NATRequest = 12,
        NATAddress = 13,
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