using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using JFramework.Interface;
using UnityEngine;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

namespace JFramework.Net
{
    [DefaultExecutionOrder(1001)]
    public partial class NetworkLobbyTransport : Transport, IEntity
    {
        public Transport transport;
        public Transport puncher;
        public bool isAwake = true;
        public float heratBeat = 3;
        public string serverId;
        public string serverKey = "Secret Key";
        public string roomName = "Game Room";
        public string roomData = "Map 1";
        public int maxPlayers = 10;
        public bool isPublic = true;
        public List<Room> rooms = new List<Room>();

        private int number;
        private bool isClient;
        private bool isServer;
        [SerializeField] private bool punching;
        private byte[] buffers;
        private UdpClient punchClient;
        private IPEndPoint punchEndPoint;
        private IPEndPoint remoteEndPoint;
        private IPEndPoint clientEndPoint;
        private SocketProxy socketProxy;
        private ConnectState clientState;
        private NetworkMediator mediator;
        private HashMap<int, int> clients = new HashMap<int, int>();
        private HashMap<int, int> connnections = new HashMap<int, int>();
        private readonly HashMap<IPEndPoint, SocketProxy> proxies = new HashMap<IPEndPoint, SocketProxy>();

        public event Action OnRoomUpdate;
        public event Action OnDisconnect;
        public bool isPunch => puncher != null;

        private void Awake()
        {
            if (transport is NetworkLobbyTransport)
            {
                Debug.Log("请使用 NetworkTransport 进行传输");
                return;
            }

            mediator = this.FindComponent<NetworkMediator>();
            if (isPunch && puncher is not NetworkTransport)
            {
                Debug.Log("请使用 NetworkTransport 进行NAT传输");
            }

            transport.OnClientConnected -= OnClientConnected;
            transport.OnClientDisconnected -= OnClientDisconnected;
            transport.OnClientReceive -= OnClientReceive;
            transport.OnClientConnected += OnClientConnected;
            transport.OnClientDisconnected += OnClientDisconnected;
            transport.OnClientReceive += OnClientReceive;

            if (isAwake)
            {
                ConnectToLobby();
            }

            InvokeRepeating(nameof(HeartBeat), heratBeat, heratBeat);

            void OnClientConnected()
            {
                clientState = ConnectState.Connected;
            }

            void OnClientDisconnected()
            {
                clientState = ConnectState.Disconnected;
                OnDisconnect?.Invoke();
            }

            void OnClientReceive(ArraySegment<byte> segment, Channel channel)
            {
                try
                {
                    ClientReceive(segment, channel);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e);
                }
            }
        }

        private void OnDestroy()
        {
            if (clientState != ConnectState.Disconnected)
            {
                transport.ClientDisconnect();
            }
        }

        public void ConnectToLobby()
        {
            if (clientState != ConnectState.Disconnected)
            {
                Debug.Log("已连接到大厅服务器!");
                return;
            }

            transport.port = port;
            transport.address = address;
            buffers = new byte[transport.GetMaxPacketSize()];
            transport.ClientConnect();
        }

        private void ClientReceive(ArraySegment<byte> segment, Channel channel)
        {
            var data = segment.Array;
            var position = segment.Offset;
            var key = data.ReadByte(ref position);
            var opcode = (OpCodes)key;
            if (key < 200)
            {
                Console.WriteLine(opcode);
            }

            if (opcode == OpCodes.Connected)
            {
                position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.Authority);
                buffers.WriteString(ref position, serverKey);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position), channel);
            }
            else if (opcode == OpCodes.Authority)
            {
                clientState = ConnectState.Authority;
                UpdateRoom();
            }
            else if (opcode == OpCodes.CreateRoom)
            {
                serverId = data.ReadString(ref position);
                UpdateRoom();
            }
            else if (opcode == OpCodes.JoinRoom)
            {
                int clientId = data.ReadInt(ref position);
                if (isClient)
                {
                    OnClientConnected?.Invoke();
                }

                if (isServer)
                {
                    clients.Add(clientId, number);
                    OnServerConnected?.Invoke(number);
                    number++;
                }
            }
            else if (opcode == OpCodes.LeaveRoom)
            {
                if (isClient)
                {
                    isClient = false;
                    OnClientDisconnected?.Invoke();
                }
            }
            else if (opcode == OpCodes.UpdateData)
            {
                var bytes = data.ReadBytes(ref position);
                if (isServer)
                {
                    if (clients.TryGetFirst(data.ReadInt(ref position), out int clientId))
                    {
                        OnServerReceive?.Invoke(clientId, new ArraySegment<byte>(bytes), channel);
                    }
                }

                if (isClient)
                {
                    OnClientReceive?.Invoke(new ArraySegment<byte>(bytes), channel);
                }
            }
            else if (opcode == OpCodes.Disconnect)
            {
                if (isServer)
                {
                    int clientId = data.ReadInt(ref position);
                    if (clients.Keys.Contains(clientId))
                    {
                        OnServerDisconnected?.Invoke(clients.GetFirst(clientId));
                        clients.Remove(clientId);
                    }
                }
            }
            else if (opcode == OpCodes.NATPuncher)
            {
                var clientIp = GetAddress();
                if (isPunch && clientIp != null)
                {
                    var punchId = data.ReadString(ref position);
                    var punchPort = (ushort)data.ReadInt(ref position);
                    if (punchClient == null)
                    {
                        punchClient = new UdpClient { ExclusiveAddressUse = false };
                        while (true)
                        {
                            try
                            {
                                punchEndPoint = new IPEndPoint(IPAddress.Parse(clientIp), Random.Range(16000, 17000));
                                punchClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                                punchClient.Client.Bind(punchEndPoint);
                                Debug.Log("内网穿透地址：" + punchEndPoint);
                                break;
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }

                    if (!IPAddress.TryParse(transport.address, out var ip))
                    {
                        ip = Dns.GetHostEntry(transport.address).AddressList[0];
                    }

                    var buffer = new byte[150];
                    position = 0;
                    buffer.WriteBool(ref position, true);
                    buffer.WriteString(ref position, punchId);
                    remoteEndPoint = new IPEndPoint(ip, punchPort);
                    for (int attempts = 0; attempts < 3; attempts++)
                    {
                        punchClient.Send(buffer, position, remoteEndPoint);
                    }

                    punchClient.BeginReceive(ReceiveData, punchClient);
                }
            }
            else if (opcode == OpCodes.NATAddress)
            {
                var newIp = data.ReadString(ref position);
                var newPort = data.ReadInt(ref position);
                var punched = data.ReadBool(ref position);
                clientEndPoint = new IPEndPoint(IPAddress.Parse(newIp), newPort);

                if (!isServer)
                {
                    if (socketProxy == null && isPunch && punched)
                    {
                        socketProxy = new SocketProxy(punchEndPoint.Port - 1);
                        socketProxy.OnReceive += ClientProcessProxyData;
                    }

                    if (isPunch && punched)
                    {
                        mediator.JoinServer("127.0.0.1", punchEndPoint.Port - 1);
                    }
                    else
                    {
                        mediator.JoinServer(newIp, newPort);
                    }

                    Debug.Log("连接穿透地址 : " + clientEndPoint);
                }
            }
        }

        private void ReceiveData(IAsyncResult result)
        {
            var endPoint = new IPEndPoint(IPAddress.Any, 0);
            var segment = punchClient.EndReceive(result, ref endPoint);
            if (!endPoint.Address.Equals(remoteEndPoint.Address))
            {
                if (isServer)
                {
                    if (proxies.TryGetFirst(endPoint, out var proxy))
                    {
                        if (segment.Length > 2)
                        {
                            proxy.ServerSend(segment, segment.Length);
                        }
                    }
                    else
                    {
                        proxies.Add(endPoint, new SocketProxy(punchEndPoint.Port + 1, endPoint));
                        proxies.GetFirst(endPoint).OnReceive += ServerProcessProxyData;
                    }
                }

                if (isClient)
                {
                    if (socketProxy == null)
                    {
                        socketProxy = new SocketProxy(punchEndPoint.Port - 1);
                        socketProxy.OnReceive += ClientProcessProxyData;
                    }
                    else
                    {
                        socketProxy.ClientSend(segment, segment.Length);
                    }
                }
            }

            punchClient.BeginReceive(ReceiveData, punchClient);
        }

        private void ServerProcessProxyData(IPEndPoint endPoint, byte[] data)
        {
            punchClient.Send(data, data.Length, endPoint);
        }

        private void ClientProcessProxyData(IPEndPoint entPoint, byte[] data)
        {
            punchClient.Send(data, data.Length, clientEndPoint);
        }

        private void HeartBeat()
        {
            if (clientState != ConnectState.Disconnected)
            {
                transport.ClientSend(new byte[] { 255 });
                punchClient?.Send(new byte[] { 0 }, 1, remoteEndPoint);
                var keys = new List<IPEndPoint>(proxies.Keys);
                foreach (var key in keys.Where(ip => DateTime.Now.Subtract(proxies.GetFirst(ip).interactTime).TotalSeconds > 10))
                {
                    proxies.GetFirst(key).Dispose();
                    proxies.Remove(key);
                }
            }
        }

        public async void UpdateRoom()
        {
            if (clientState == ConnectState.Authority)
            {
                var uri = $"http://{address}:{port}/api/compressed/servers";
                using var request = UnityWebRequest.Get(uri);
                await request.SendWebRequest();
                var result = request.downloadHandler.text;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("无法获取服务器列表。" + $"{address}:{port}");
                    return;
                }

                rooms?.Clear();
                var json = result.Decompress();
                json = "{" + "\"value\":" + json + "}";
                Debug.Log("房间信息：" + json);
                rooms = JsonUtility.FromJson<Variables<Room>>(json).value;
                OnRoomUpdate?.Invoke();
            }
            else
            {
                Debug.Log("您必须连接到大厅以请求房间列表!");
            }
        }

        public void UpdateRoom(string serverName, string serverData, bool isPublic, int maxPlayers)
        {
            if (isServer)
            {
                int position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.UpdateRoom);
                buffers.WriteString(ref position, serverName);
                buffers.WriteString(ref position, serverData);
                buffers.WriteBool(ref position, isPublic);
                buffers.WriteInt(ref position, maxPlayers);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
            }
        }

        private static string GetAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
    }

    public partial class NetworkLobbyTransport
    {
        public override void ClientConnect(Uri uri = null)
        {
            if (uri != null)
            {
                address = uri.Host;
            }

            if (clientState == ConnectState.Disconnected)
            {
                Debug.Log("没有连接到大厅!");
                OnClientDisconnected?.Invoke();
                return;
            }

            if (isClient || isServer)
            {
                Debug.Log("客户端或服务器已经连接!");
                return;
            }

            int position = 0;
            punching = false;
            buffers.WriteByte(ref position, (byte)OpCodes.JoinRoom);
            buffers.WriteString(ref position, transport.address);
            buffers.WriteBool(ref position, isPunch);

            if (!isPunch)
            {
                buffers.WriteString(ref position, "0.0.0.0");
            }
            else
            {
                buffers.WriteString(ref position, GetAddress());
            }

            isClient = true;
            transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void ClientSend(ArraySegment<byte> segment, Channel channel = Channel.Reliable)
        {
            if (punching)
            {
                mediator.ClientSend(segment, channel);
            }
            else
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.UpdateData);
                buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                buffers.WriteInt(ref position, 0);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position), channel);
            }
        }

        public override void ClientDisconnect()
        {
            isClient = false;
            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
            transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
            mediator.ClientDisconnect();
        }

        public override void StartServer()
        {
            if (clientState == ConnectState.Disconnected)
            {
                Debug.Log("没有连接到大厅!");
                return;
            }

            if (isClient || isServer)
            {
                Debug.Log("客户端或服务器已经连接!");
                return;
            }

            number = 1;
            isServer = true;
            clients = new HashMap<int, int>();
            connnections = new HashMap<int, int>();

            var keys = proxies.Keys.ToList();
            foreach (var key in keys)
            {
                proxies.GetFirst(key).Dispose();
                proxies.Remove(key);
            }

            var position = 0;
            var clientIp = GetAddress();
            buffers.WriteByte(ref position, (byte)OpCodes.CreateRoom);
            buffers.WriteString(ref position, roomName);
            buffers.WriteString(ref position, roomData);
            buffers.WriteInt(ref position, maxPlayers);
            buffers.WriteBool(ref position, isPublic);
            if (isPunch && clientIp != null)
            {
                buffers.WriteString(ref position, clientIp);
                mediator.StartServer(punchEndPoint.Port + 1);
                Debug.Log("内网穿透服务器:" + punchEndPoint.Address + ":" + (punchEndPoint.Port + 1));
            }
            else
            {
                buffers.WriteString(ref position, "0.0.0.0");
            }

            buffers.WriteInt(ref position, isPunch ? punchEndPoint.Port + 1 : 1);
            buffers.WriteBool(ref position, isPunch);
            buffers.WriteBool(ref position, isPunch && clientIp != null);
            transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void ServerSend(int clientId, ArraySegment<byte> segment, Channel channel = Channel.Reliable)
        {
            if (isPunch && connnections.TryGetSecond(clientId, out int connection))
            {
                mediator.ServerSend(connection, segment, channel);
            }
            else
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.UpdateData);
                buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                buffers.WriteInt(ref position, clients.GetSecond(clientId));
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position), channel);
            }
        }

        public override void ServerDisconnect(int clientId)
        {
            if (clients.TryGetSecond(clientId, out int owner))
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.Disconnect);
                buffers.WriteInt(ref position, owner);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
                return;
            }

            if (connnections.TryGetSecond(clientId, out int connection))
            {
                mediator.ServerDisconnect(connection);
            }
        }

        public override Uri GetServerUri()
        {
            var builder = new UriBuilder
            {
                Scheme = "LRM",
                Host = serverId
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
                mediator.StopServer();
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
            mediator.ClientEarlyUpdate();
        }

        public override void ClientAfterUpdate()
        {
            transport.ClientAfterUpdate();
            mediator.ClientAfterUpdate();
        }

        public override void ServerEarlyUpdate()
        {
            mediator.ServerEarlyUpdate();
        }

        public override void ServerAfterUpdate()
        {
            mediator.ServerAfterUpdate();
        }
    }

    public partial class NetworkLobbyTransport
    {
        public void NATServerConnected(int clientId)
        {
            if (isServer)
            {
                Debug.Log($"客户端 {clientId} 加入NAT服务器");
                connnections.Add(clientId, number);
                OnServerConnected?.Invoke(number);
                number++;
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
            if (isServer)
            {
                Debug.Log($"客户端 {clientId} 从NAT服务器断开");
                OnServerDisconnected?.Invoke(connnections.GetFirst(clientId));
                connnections.Remove(clientId);
            }
        }

        public void NATClientConnected()
        {
            punching = true;
            Debug.Log("加入NAT服务器");
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
            if (punching)
            {
                isClient = false;
                punching = false;
                Debug.Log("从NAT服务器断开");
                OnClientDisconnected?.Invoke();
            }
            else
            {
                isClient = true;
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.JoinRoom);
                buffers.WriteString(ref position, transport.address);
                buffers.WriteBool(ref position, false);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
                Debug.Log("从NAT服务器断开，切换至中继服务器。");
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
            public string id;
            public string name;
            public string data;
            public int count;
            public int current;
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
            NATPuncher = 9,
            NATAddress = 10,
        }
    }
}