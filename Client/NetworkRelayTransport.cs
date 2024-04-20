using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

namespace JFramework.Net
{
    [DefaultExecutionOrder(1001)]
    public partial class NetworkRelayTransport : Transport
    {
        public Transport transport;
        public int serverId = -1;
        public bool isAwake = true;
        public float heratBeat = 3;
        public string authority = "Secret Auth Key";

        public bool isPunch = true;
        public ushort punchPort = 7776;

        public string serverName = "Game Room";
        public string serverData = "Map 1";
        public int maxPlayers = 10;
        public bool isPublic = true;
        public List<Room> rooms = new List<Room>();

        public event Action OnRoomUpdate;
        public event Action OnDisconnect;

        private bool isNAT;
        private bool isInit;
        private bool isRelay;
        private bool isClient;
        private bool isServer;
        private bool isActive;
        private int hostId;
        private int memberId;
        private byte[] buffers;
        private UdpClient clientProxy;
        private IPEndPoint natEndPoint;
        private IPEndPoint relayEndPoint;
        private IPEndPoint directEndPoint;
        private SocketProxy socketProxy;
        private NetworkNATPuncher puncher;
        private readonly byte[] punchData = { 1 };
        private HashMap<int, int> connnections = new HashMap<int, int>();
        private HashMap<int, int> relayClients = new HashMap<int, int>();
        private readonly HashMap<IPEndPoint, SocketProxy> proxies = new HashMap<IPEndPoint, SocketProxy>();

        private void Awake()
        {
            if (transport is NetworkRelayTransport)
            {
                throw new Exception("需要使用一个不同的传输。");
            }

            puncher = GetComponent<NetworkNATPuncher>();

            if (puncher != null)
            {
                if (isPunch && !puncher.IsPunch())
                {
                    Debug.LogWarning("NATPunch已打开，但所使用的传输机制不支持。它将被禁用。");
                    isPunch = false;
                }
            }

            if (!isInit)
            {
                isInit = true;
                transport.OnClientConnected = ClientConnected;
                transport.OnClientReceive = ClientReceive;
                transport.OnClientDisconnected = ClientDisconnected;
            }

            if (isAwake)
            {
                ConnectToRelay();
            }

            InvokeRepeating(nameof(SendHeartBeat), heratBeat, heratBeat);

            void ClientConnected()
            {
                isRelay = true;
            }

            void ClientDisconnected()
            {
                isActive = false;
                isRelay = false;
                OnDisconnect?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (isActive)
            {
                transport.ClientDisconnect();
            }
        }

        public void ConnectToRelay()
        {
            if (!isRelay)
            {
                buffers = new byte[transport.GetMaxPacketSize()];
                transport.address = address;
                transport.port = port;
                transport.ClientConnect();
            }
            else
            {
                Debug.Log("已连接到中继服务器!");
            }
        }

        private void ClientReceive(ArraySegment<byte> segment, Channel channel)
        {
            try
            {
                var data = segment.Array;
                var position = segment.Offset;
                var key = data.ReadByte(ref position);
                var opcode = (OpCodes)key;
                if (key < 200)
                {
                    Console.WriteLine(opcode);
                }
                if (opcode == OpCodes.Authority)
                {
                    isActive = true;
                    RequestServerList();
                }
                else if (opcode == OpCodes.AuthorityRequest)
                {
                    position = 0;
                    buffers.WriteByte(ref position, (byte)OpCodes.AuthorityResponse);
                    buffers.WriteString(ref position, authority);
                    transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
                }
                else if (opcode == OpCodes.ReceiveData)
                {
                    var receive = data.ReadBytes(ref position);
                    if (isServer)
                    {
                        if (relayClients.TryGetFirst(data.ReadInt(ref position), out int clientId))
                        {
                            OnServerReceive?.Invoke(clientId, new ArraySegment<byte>(receive), channel);
                        }
                    }

                    if (isClient)
                    {
                        OnClientReceive?.Invoke(new ArraySegment<byte>(receive), channel);
                    }
                }
                else if (opcode == OpCodes.LeaveServer)
                {
                    if (isClient)
                    {
                        isClient = false;
                        OnClientDisconnected?.Invoke();
                    }
                    RequestServerList();
                }
                else if (opcode == OpCodes.Disconnect)
                {
                    if (isServer)
                    {
                        int user = data.ReadInt(ref position);
                        if (relayClients.TryGetFirst(user, out int clientId))
                        {
                            OnServerDisconnected?.Invoke(relayClients.GetFirst(clientId));
                            relayClients.Remove(user);
                        }
                    }
                }
                else if (opcode == OpCodes.CreateRoomAfter)
                {
                    serverId = data.ReadInt(ref position);
                    RequestServerList();
                }
                else if (opcode == OpCodes.JoinServerAfter)
                {
                    int clientId = data.ReadInt(ref position);
                    if (isClient)
                    {
                        OnClientConnected?.Invoke();
                    }

                    if (isServer)
                    {
                        relayClients.Add(clientId, memberId);
                        OnServerConnected?.Invoke(memberId);
                        memberId++;
                    }
                }
                else if (opcode == OpCodes.NATAddress)
                {
                    var directIp = data.ReadString(ref position);
                    int directPort = data.ReadInt(ref position);
                    bool attemptNatPunch = data.ReadBool(ref position);

                    directEndPoint = new IPEndPoint(IPAddress.Parse(directIp), directPort);

                    if (isPunch && attemptNatPunch)
                    {
                        StartCoroutine(NATPunch(directEndPoint));
                    }

                    if (!isServer)
                    {
                        if (socketProxy == null && isPunch && attemptNatPunch)
                        {
                            socketProxy = new SocketProxy(natEndPoint.Port - 1);
                            socketProxy.OnReceive += ClientProcessProxyData;
                        }

                        if (isPunch && attemptNatPunch)
                        {
                            puncher.JoinServer("127.0.0.1", natEndPoint.Port - 1);
                        }
                        else
                        {
                            puncher.JoinServer(directIp, directPort);
                        }
                    }
                }
                else if (opcode == OpCodes.NATRequest)
                {
                    if (isPunch && GetLocalIp() != null && puncher != null)
                    {
                        var buffer = new byte[150];
                        int sendPos = 0;
                        
                        buffer.WriteBool(ref sendPos, true);
                        buffer.WriteString(ref sendPos, data.ReadString(ref position));
                        punchPort = (ushort)data.ReadInt(ref position);
                        if (clientProxy == null)
                        {
                            clientProxy = new UdpClient { ExclusiveAddressUse = false };
                            while (true)
                            {
                                try
                                {
                                    natEndPoint = new IPEndPoint(IPAddress.Parse(GetLocalIp()), Random.Range(16000, 17000));
                                    clientProxy.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                                    clientProxy.Client.Bind(natEndPoint);
                                    break;
                                }
                                catch
                                {
                                    // ignored
                                }
                            }
                        }
                        if (!IPAddress.TryParse(address, out var ip))
                        {
                            address = Dns.GetHostEntry(address).AddressList[0].ToString();
                        }
                        else
                        {
                            address = ip.ToString();
                        }

                        for (int attempts = 0; attempts < 3; attempts++)
                        {
                            relayEndPoint = new IPEndPoint(IPAddress.Parse(address), punchPort);
                        }
                        
                        clientProxy.Send(buffer, sendPos, relayEndPoint);
                        clientProxy.BeginReceive(ReceiveData, clientProxy);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(e.ToString());
            }
        }

        private void ReceiveData(IAsyncResult result)
        {
            var endPoint = new IPEndPoint(IPAddress.Any, 0);
            var data = clientProxy.EndReceive(result, ref endPoint);

            if (!endPoint.Address.Equals(relayEndPoint.Address))
            {
                if (isServer)
                {
                    if (proxies.TryGetFirst(endPoint, out SocketProxy foundProxy))
                    {
                        if (data.Length > 2)
                        {
                            foundProxy.RelayData(data, data.Length);
                        }
                    }
                    else
                    {
                        proxies.Add(endPoint, new SocketProxy(natEndPoint.Port + 1, endPoint));
                        proxies.GetFirst(endPoint).OnReceive += ServerProcessProxyData;
                    }
                }

                if (isClient)
                {
                    if (socketProxy == null)
                    {
                        socketProxy = new SocketProxy(natEndPoint.Port - 1);
                        socketProxy.OnReceive += ClientProcessProxyData;
                    }
                    else
                    {
                        socketProxy.ClientRelayData(data, data.Length);
                    }
                }
            }

            clientProxy.BeginReceive(ReceiveData, clientProxy);
        }

        private void ServerProcessProxyData(IPEndPoint remoteEndpoint, byte[] data)
        {
            clientProxy.Send(data, data.Length, remoteEndpoint);
        }

        private void ClientProcessProxyData(IPEndPoint _, byte[] data)
        {
            clientProxy.Send(data, data.Length, directEndPoint);
        }

        private void SendHeartBeat()
        {
            if (isRelay)
            {
                int position = 0;
                buffers.WriteByte(ref position, 200);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));

                clientProxy?.Send(new byte[] { 0 }, 1, relayEndPoint);

                var keys = new List<IPEndPoint>(proxies.Keys);

                foreach (var key in keys)
                {
                    if (DateTime.Now.Subtract(proxies.GetFirst(key).interactTime).TotalSeconds > 10)
                    {
                        proxies.GetFirst(key).Dispose();
                        proxies.Remove(key);
                    }
                }
            }
        }

        public void RequestServerList()
        {
            if (isActive && isRelay)
            {
                GetServerList();
            }
            else
            {
                Debug.Log("您必须连接到中继以请求服务器列表!");
            }
        }

        private IEnumerator NATPunch(IPEndPoint remoteAddress)
        {
            for (int i = 0; i < 10; i++)
            {
                clientProxy.Send(punchData, 1, remoteAddress);
                yield return new WaitForSeconds(0.25f);
            }
        }

        private async void GetServerList()
        {
            var uri = $"http://{address}:{port}/api/compressed/servers";
            using var request = UnityWebRequest.Get(uri);
            await request.SendWebRequest();
            var result = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("无法获取服务器列表。");
                return;
            }

            rooms?.Clear();
            var json = result.Decompress();
            json = "{" + "\"value\":" + json + "}";
            Debug.Log("房间信息："+json);
            rooms = JsonUtility.FromJson<Variables<Room>>(json).value;
            OnRoomUpdate?.Invoke();
        }

        public void UpdateRoomInfo(string serverName = null, string serverData = null, bool? isPublic = null, int? playerCap = null)
        {
            if (isServer)
            {
                int position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.UpdateRoom);
                if (!string.IsNullOrEmpty(serverName))
                {
                    buffers.WriteBool(ref position, true);
                    buffers.WriteString(ref position, serverName);
                }
                else
                {
                    buffers.WriteBool(ref position, false);
                }

                if (!string.IsNullOrEmpty(serverData))
                {
                    buffers.WriteBool(ref position, true);
                    buffers.WriteString(ref position, serverData);
                }
                else
                {
                    buffers.WriteBool(ref position, false);
                }

                if (isPublic != null)
                {
                    buffers.WriteBool(ref position, true);
                    buffers.WriteBool(ref position, isPublic.Value);
                }
                else
                {
                    buffers.WriteBool(ref position, false);
                }

                if (playerCap != null)
                {
                    buffers.WriteBool(ref position, true);
                    buffers.WriteInt(ref position, playerCap.Value);
                }
                else
                {
                    buffers.WriteBool(ref position, false);
                }

                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
            }
        }

        private static string GetLocalIp()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
    }

    public partial class NetworkRelayTransport
    {
        public override void ClientConnect(Uri uri = null)
        {
            if (uri != null)
            {
                address = uri.Host;
            }

            if (!isRelay)
            {
                Debug.Log("没有连接到中继或无效的服务器id!");
                OnClientDisconnected?.Invoke();
                return;
            }

            if (isClient || isServer)
            {
                throw new Exception("托管时无法连接/已经连接!");
            }

            int.TryParse(address, out hostId);
            int position = 0;
            isNAT = false;
            buffers.WriteByte(ref position, (byte)OpCodes.JoinServer);
            buffers.WriteInt(ref position, hostId);
            buffers.WriteBool(ref position, puncher != null);

            if (GetLocalIp() == null)
            {
                buffers.WriteString(ref position, "0.0.0.0");
            }
            else
            {
                buffers.WriteString(ref position, GetLocalIp());
            }

            isClient = true;
            transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void ClientSend(ArraySegment<byte> segment, Channel channel = Channel.Reliable)
        {
            if (isNAT)
            {
                puncher.ClientSend(segment, channel);
            }
            else
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.SendData);
                buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                buffers.WriteInt(ref position, 0);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
            }
        }

        public override void ClientDisconnect()
        {
            isClient = false;
            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
            transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));

            if (isPunch && puncher != null)
            {
                puncher.ClientDisconnect();
            }
        }

        public override void StartServer()
        {
            if (!isRelay)
            {
                Debug.Log("没有连接到中继，服务器启动失败。");
                return;
            }

            if (isClient || isServer)
            {
                Debug.Log("不能托管，因为已经托管或连接!");
                return;
            }

            isServer = true;
            memberId = 1;
            relayClients = new HashMap<int, int>();
            connnections = new HashMap<int, int>();

            var keys = new List<IPEndPoint>(proxies.Keys);
            foreach (var key in keys)
            {
                proxies.GetFirst(key).Dispose();
                proxies.Remove(key);
            }

            int position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.CreateRoom);
            buffers.WriteInt(ref position, maxPlayers);
            buffers.WriteString(ref position, serverName);
            buffers.WriteBool(ref position, isPublic);
            buffers.WriteString(ref position, serverData);
            buffers.WriteBool(ref position, puncher != null && GetLocalIp() != null);
            if (puncher != null && GetLocalIp() != null && isPunch)
            {
                buffers.WriteString(ref position, GetLocalIp());
                puncher.StartServer(isPunch ? natEndPoint.Port + 1 : -1);
            }
            else
            {
                buffers.WriteString(ref position, "0.0.0.0");
            }

            if (isPunch)
            {
                buffers.WriteBool(ref position, true);
                buffers.WriteInt(ref position, 0);
            }
            else
            {
                buffers.WriteBool(ref position, false);
                buffers.WriteInt(ref position, puncher == null ? 1 : puncher.IsPunch() ? puncher.GetTransportPort() : 1);
            }

            transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void ServerSend(int clientId, ArraySegment<byte> segment, Channel channel = Channel.Reliable)
        {
            if (puncher != null && connnections.TryGetSecond(clientId, out int directId))
            {
                puncher.ServerSend(directId, segment, channel);
            }
            else
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.SendData);
                buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                buffers.WriteInt(ref position, relayClients.GetSecond(clientId));
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position), channel);
            }
        }

        public override void ServerDisconnect(int clientId)
        {
            if (relayClients.TryGetSecond(clientId, out int relayId))
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.RemoveClient);
                buffers.WriteInt(ref position, relayId);
            }

            if (connnections.TryGetSecond(clientId, out int connection))
            {
                puncher.ServerDisconnect(connection);
            }
        }

        public override Uri GetServerUri()
        {
            var builder = new UriBuilder
            {
                Scheme = "LRM",
                Host = serverId.ToString()
            };

            return builder.Uri;
        }

        public override void StopServer()
        {
            if (isServer)
            {
                isServer = false;
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));

                if (puncher != null)
                {
                    puncher.StopServer();
                }

                var keys = new List<IPEndPoint>(proxies.Keys);
                foreach (var key in keys)
                {
                    proxies.GetFirst(key).Dispose();
                    proxies.Remove(key);
                }
            }
        }

        public override int GetMaxPacketSize(Channel channel = Channel.Reliable)
        {
            return transport.GetMaxPacketSize(channel);
        }

        public override int UnreliableSize()
        {
            return transport.UnreliableSize();
        }

        public override void ClientEarlyUpdate()
        {
            transport.ClientEarlyUpdate();

            if (puncher != null)
            {
                puncher.transport.ClientEarlyUpdate();
            }
        }

        public override void ClientAfterUpdate()
        {
            transport.ClientAfterUpdate();

            if (puncher != null)
            {
                puncher.transport.ClientAfterUpdate();
            }
        }

        public override void ServerEarlyUpdate()
        {
            if (puncher != null)
            {
                puncher.transport.ServerEarlyUpdate();
            }
        }

        public override void ServerAfterUpdate()
        {
            if (puncher != null)
            {
                puncher.transport.ServerAfterUpdate();
            }
        }
    }

    public partial class NetworkRelayTransport
    {
        public void NATServerConnected(int clientId)
        {
            if (isServer)
            {
                connnections.Add(clientId, memberId);
                OnServerConnected?.Invoke(memberId);
                memberId++;
            }
        }

        public void NATServerReceive(int clientId, ArraySegment<byte> segment, Channel channel)
        {
            if (isServer)
            {
                OnServerReceive?.Invoke(connnections.GetFirst(clientId), segment, channel);
            }
        }

        public void NATServerDisconnected(int clientId)
        {
            if (!isServer)
            {
                OnServerDisconnected?.Invoke(connnections.GetFirst(clientId));
                connnections.Remove(clientId);
            }
        }

        public void NATClientConnected()
        {
            isNAT = true;
            OnClientConnected?.Invoke();
        }

        public void NATClientReceive(ArraySegment<byte> segment, Channel channel)
        {
            if (isClient)
            {
                OnClientReceive?.Invoke(segment, channel);
            }
        }

        public void NATClientDisconnected()
        {
            if (isNAT)
            {
                isNAT = false;
                isClient = false;
                OnClientDisconnected?.Invoke();
            }
            else
            {
                var position = 0;
                isClient = true;
                isNAT = false;
                buffers.WriteByte(ref position, (byte)OpCodes.JoinServer);
                buffers.WriteInt(ref position, hostId);
                buffers.WriteBool(ref position, false);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
            }

            if (socketProxy != null)
            {
                socketProxy.Dispose();
                socketProxy = null;
            }
        }

        [Serializable]
        public struct Room
        {
            public string serverName;
            public int currentPlayers;
            public int maxPlayers;
            public int serverId;
            public string serverData;
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
    }
}