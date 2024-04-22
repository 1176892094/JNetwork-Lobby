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
        Connected = 1,
        Authority = 2,
        JoinRoom = 3,
        CreateRoom = 4,
        UpdateRoom = 5,
        LeaveRoom = 6,
        UpdateData = 7,
        Disconnect = 8,
        NATRequest = 9,
        NATAddress = 10,
    }
}