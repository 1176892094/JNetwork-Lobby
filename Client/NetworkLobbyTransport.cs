using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using JFramework.Core;
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

        private int playerId;
        private bool isClient;
        private bool isServer;
        private bool punching;
        private byte[] buffers;
        private UdpClient punchClient;
        private IPEndPoint punchEndPoint;
        private IPEndPoint serverEndPoint;
        private IPEndPoint remoteEndPoint;
        private SocketProxy clientProxy;
        private ConnectState clientState;
        private NetworkMediator mediator;
        private readonly HashMap<int, int> clients = new HashMap<int, int>();
        private readonly HashMap<int, int> connnections = new HashMap<int, int>();
        private readonly HashMap<IPEndPoint, SocketProxy> proxies = new HashMap<IPEndPoint, SocketProxy>();

        public event Action OnRoomUpdate;
        public event Action OnDisconnect;
        public bool isPunch => puncher != null;

        public string currentIp
        {
            get
            {
                try
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                    foreach (var inter in interfaces)
                    {
                        if (inter.OperationalStatus == OperationalStatus.Up && inter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        {
                            var properties = inter.GetIPProperties();
                            var ip = properties.UnicastAddresses.FirstOrDefault(
                                ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);
                            if (ip != null)
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }

                    // 虚拟机无法解析网络接口 因此额外解析主机地址
                    var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                    foreach (var ip in addresses)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return ip.ToString();
                        }
                    }

                    return "127.0.0.1";
                }
                catch
                {
                    return "127.0.0.1";
                }
            }
        }

        private void Awake()
        {
            if (transport is NetworkLobbyTransport)
            {
                Debug.LogWarning("请使用 NetworkTransport 进行传输！");
                return;
            }

            mediator = this.FindComponent<NetworkMediator>();
            if (isPunch)
            {
                if (puncher is not NetworkTransport)
                {
                    Debug.LogWarning("请使用 NetworkTransport 进行NAT传输！");
                    return;
                }

                if (puncher == transport)
                {
                    Debug.LogWarning("中继传输 和 NAT传输 不能相同！");
                    return;
                }
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
            DisconnectToLobby();
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

        public void DisconnectToLobby()
        {
            if (clientState == ConnectState.Disconnected)
            {
                Debug.Log("大厅服务器已停止!");
                return;
            }
            
            if (isPunch)
            {
                mediator.StopServer();
                punchClient?.Dispose();
                punchClient = null;
                punching = false;
                clientProxy?.Dispose();
                clientProxy = null;
            }

            isServer = false;
            isClient = false;
            clients.Clear();
            connnections.Clear();
            proxies.Clear();
            clientState = ConnectState.Disconnected;
            transport.ClientDisconnect();
        }

        private void ClientReceive(ArraySegment<byte> segment, Channel channel)
        {
            var data = segment.Array;
            var position = segment.Offset;
            var opcode = (OpCodes)data.ReadByte(ref position);
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
            }
            else if (opcode == OpCodes.JoinRoom)
            {
                if (isServer)
                {
                    clients.Add(data.ReadInt(ref position), playerId);
                    OnServerConnected?.Invoke(playerId);
                    playerId++;
                }

                if (isClient)
                {
                    OnClientConnected?.Invoke();
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
                var readBytes = data.ReadBytes(ref position);
                if (isServer)
                {
                    if (clients.TryGetFirst(data.ReadInt(ref position), out int clientId))
                    {
                        OnServerReceive?.Invoke(clientId, new ArraySegment<byte>(readBytes), channel);
                    }
                }

                if (isClient)
                {
                    OnClientReceive?.Invoke(new ArraySegment<byte>(readBytes), channel);
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
                if (!isPunch) return;
                var punchId = data.ReadString(ref position);
                var punchPort = data.ReadInt(ref position);
                if (punchClient == null)
                {
                    punchClient = new UdpClient { ExclusiveAddressUse = false };
                    while (true)
                    {
                        try
                        {
                            punchEndPoint = new IPEndPoint(IPAddress.Parse(currentIp), Random.Range(16000, 17000));
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
                serverEndPoint = new IPEndPoint(ip, punchPort);
                for (int attempts = 0; attempts < 3; attempts++)
                {
                    punchClient.Send(buffer, position, serverEndPoint);
                }

                punchClient.BeginReceive(ReceiveData, punchClient);
            }
            else if (opcode == OpCodes.NATAddress)
            {
                var newIp = data.ReadString(ref position);
                var newPort = data.ReadInt(ref position);
                var punched = data.ReadBool(ref position);
                remoteEndPoint = new IPEndPoint(IPAddress.Parse(newIp), newPort);

                Debug.Log("连接穿透地址 : " + remoteEndPoint);
                if (punched)
                {
                    NATPunch(remoteEndPoint);
                }

                if (!isServer)
                {
                    if (clientProxy == null && isPunch && punched)
                    {
                        clientProxy = new SocketProxy(punchEndPoint.Port - 1, ClentSend);
                    }

                    mediator.JoinServer("127.0.0.1", punchEndPoint.Port - 1);
                }
            }
        }

        private async void NATPunch(IPEndPoint remoteEndPoint)
        {
            for (int i = 0; i < 10; i++)
            {
                await punchClient.SendAsync(new[] { byte.MaxValue }, 1, remoteEndPoint);
                await Task.Delay(250);
            }
        }

        private void ReceiveData(IAsyncResult result)
        {
            var endPoint = new IPEndPoint(IPAddress.Any, 0);
            var segment = punchClient.EndReceive(result, ref endPoint);
            if (!endPoint.Equals(serverEndPoint))
            {
                if (segment.Length < 2)
                {
                    Debug.Log($"接收来自 {endPoint} 的消息: {BitConverter.ToString(segment, 0, segment.Length)}");
                }

                if (isServer)
                {
                    if (proxies.TryGetFirst(endPoint, out var proxy))
                    {
                        if (segment.Length > 2)
                        {
                            proxy.ServerSend(segment);
                        }
                    }
                    else
                    {
                        proxies.Add(endPoint, new SocketProxy(punchEndPoint.Port + 1, ServerSend, endPoint));
                    }
                }

                if (isClient)
                {
                    if (clientProxy != null)
                    {
                        clientProxy.ClientSend(segment);
                    }
                    else
                    {
                        clientProxy = new SocketProxy(punchEndPoint.Port - 1, ClentSend);
                    }
                }
            }

            punchClient.BeginReceive(ReceiveData, punchClient);
        }

        private void ServerSend(IPEndPoint endPoint, byte[] data)
        {
            punchClient.Send(data, data.Length, endPoint);
        }

        private void ClentSend(byte[] data)
        {
            punchClient.Send(data, data.Length, remoteEndPoint);
        }

        private void HeartBeat()
        {
            if (clientState != ConnectState.Disconnected)
            {
                transport.ClientSend(new[] { byte.MaxValue });
                punchClient?.Send(new byte[] { 0 }, 1, serverEndPoint);
                var keys = proxies.Keys.ToList();
                foreach (var key in keys.Where(ip => proxies.GetFirst(ip).interval > 10))
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
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("无法获取服务器列表。" + $"{address}:{port}");
                    return;
                }

                var json = "{" + "\"value\":" + request.downloadHandler.text.Decompress() + "}";
                Debug.Log("房间信息：" + json);
                rooms = JsonManager.Reader<List<Room>>(json);
                OnRoomUpdate?.Invoke();
            }
            else
            {
                Debug.Log("您必须连接到大厅以请求房间列表!");
            }
        }

        public void UpdateRoom(string roomName, string roomData, bool isPublic, int maxPlayers)
        {
            if (isServer)
            {
                int position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.UpdateRoom);
                buffers.WriteString(ref position, roomName);
                buffers.WriteString(ref position, roomData);
                buffers.WriteBool(ref position, isPublic);
                buffers.WriteInt(ref position, maxPlayers);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
            }
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

            playerId = 1;
            isServer = true;
            clients.Clear();
            connnections.Clear();

            var keys = proxies.Keys.ToList();
            foreach (var key in keys)
            {
                proxies.GetFirst(key).Dispose();
                proxies.Remove(key);
            }

            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.CreateRoom);
            buffers.WriteString(ref position, roomName);
            buffers.WriteString(ref position, roomData);
            buffers.WriteInt(ref position, maxPlayers);
            buffers.WriteBool(ref position, isPublic);
            if (isPunch)
            {
                buffers.WriteString(ref position, punchEndPoint.Address.ToString());
                buffers.WriteInt(ref position, punchEndPoint.Port + 1);
                mediator.StartServer(punchEndPoint.Port + 1);
                Debug.Log("NAT服务器地址:" + punchEndPoint.Address + ":" + (punchEndPoint.Port + 1));
            }
            else
            {
                buffers.WriteString(ref position, "0.0.0.0");
                buffers.WriteInt(ref position, 0);
            }


            buffers.WriteBool(ref position, isPunch);
            buffers.WriteBool(ref position, isPunch && currentIp != "127.0.0.1");
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
            if (clients.TryGetSecond(clientId, out int ownerId))
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.Disconnect);
                buffers.WriteInt(ref position, ownerId);
                transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
                return;
            }

            if (connnections.TryGetSecond(clientId, out int connection))
            {
                mediator.ServerDisconnect(connection);
            }
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
                var keys = proxies.Keys.ToList();
                foreach (var key in keys)
                {
                    proxies.GetFirst(key).Dispose();
                    proxies.Remove(key);
                }
            }
        }

        public override Uri GetServerUri()
        {
            var builder = new UriBuilder
            {
                Scheme = "UDP",
                Host = serverId
            };

            return builder.Uri;
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
                connnections.Add(clientId, playerId);
                OnServerConnected?.Invoke(playerId);
                playerId++;
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

            if (clientProxy != null)
            {
                clientProxy.Dispose();
                clientProxy = null;
            }
        }

        [Serializable]
        public struct Room
        {
            public string id;
            public string name;
            public string data;
            public bool isPublic;
            public int maxCount;
            public List<int> clients;
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