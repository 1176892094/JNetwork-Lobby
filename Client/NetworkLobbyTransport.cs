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
    public partial class NetworkLobbyTransport : Transport
    {
        public Transport transport;
        public bool isAwake = true;
        public float heratBeat = 3;
        public bool isPunch;
        public string serverId;
        public string serverKey = "Secret Key";
        public string roomName = "Game Room";
        public string roomData = "Map 1";
        public int maxPlayers = 10;
        public bool isPublic = true;
        public List<Room> rooms = new List<Room>();

        public event Action OnRoomUpdate;
        public event Action OnDisconnect;

        private int index;
        private bool isInit;
        private bool isLobby;
        private bool isClient;
        private bool isServer;
        private bool isActive;
        private bool punching;
        private byte[] buffers;
        private UdpClient punchClient;
        private IPEndPoint punchEndPoint;
        private IPEndPoint remoteEndPoint;
        private IPEndPoint clientEndPoint;
        private SocketProxy socketProxy;
        private NetworkProxyTransport puncher;
        private HashMap<int, int> clients = new HashMap<int, int>();
        private HashMap<int, int> connnections = new HashMap<int, int>();
        private readonly HashMap<IPEndPoint, SocketProxy> proxies = new HashMap<IPEndPoint, SocketProxy>();

        private void Awake()
        {
            if (transport is NetworkLobbyTransport)
            {
                Debug.Log("请使用 NetworkTransport 进行传输");
                return;
            }

            puncher = GetComponentInChildren<NetworkProxyTransport>();
            if (isPunch && puncher != null)
            {
                Debug.Log("请使用 NetworkTransport 进行NAT传输");
                isPunch = false;
            }

            if (!isInit)
            {
                isInit = true;
                transport.OnClientConnected = OnClientConnected;
                transport.OnClientDisconnected = OnClientDisconnected;
                transport.OnClientReceive = OnClientReceive;
            }

            if (isAwake)
            {
                ConnectToLobby();
            }

            InvokeRepeating(nameof(HeartBeat), heratBeat, heratBeat);

            void OnClientConnected()
            {
                isLobby = true;
            }

            void OnClientDisconnected()
            {
                isLobby = false;
                isActive = false;
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
            if (isActive)
            {
                transport.ClientDisconnect();
            }
        }

        public void ConnectToLobby()
        {
            if (!isLobby)
            {
                transport.port = port;
                transport.address = address;
                buffers = new byte[transport.GetMaxPacketSize()];
                transport.ClientConnect();
            }
            else
            {
                Debug.Log("已连接到大厅服务器!");
            }
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
                isActive = true;
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
                    clients.Add(clientId, index);
                    OnServerConnected?.Invoke(index);
                    index++;
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
                    int client = data.ReadInt(ref position);
                    if (clients.TryGetFirst(client, out int clientId))
                    {
                        OnServerDisconnected?.Invoke(clients.GetFirst(client));
                        clients.Remove(client);
                    }
                }
            }
            else if (opcode == OpCodes.NATPuncher)
            {
                var clientIp = GetAddress();
                if (isPunch && puncher != null && clientIp != null)
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
                                Debug.LogWarning(punchEndPoint);
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

                Debug.LogWarning(clientEndPoint);
                if (isPunch && punched)
                {
                    StartCoroutine(NATPunch(clientEndPoint));
                }

                if (!isServer)
                {
                    if (socketProxy == null && isPunch && punched)
                    {
                        socketProxy = new SocketProxy(punchEndPoint.Port - 1);
                        socketProxy.OnReceive += ClientProcessProxyData;
                    }

                    if (isPunch && punched)
                    {
                        if (newIp == "127.0.0.1")
                        {
                            puncher.JoinServer("127.0.0.1", newPort + 1);
                        }
                        else
                        {
                            puncher.JoinServer("127.0.0.1", punchEndPoint.Port - 1);
                        }
                    }
                    else
                    {
                        puncher.JoinServer(newIp, newPort);
                    }
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
                        Debug.LogWarning(endPoint);
                        Debug.LogWarning(punchEndPoint);
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
            Debug.LogWarning("Server:" + endPoint);
            punchClient.Send(data, data.Length, endPoint);
        }

        private void ClientProcessProxyData(IPEndPoint entPoint, byte[] data)
        {
            Debug.LogWarning("Client:" + entPoint);
            punchClient.Send(data, data.Length, clientEndPoint);
        }

        private void HeartBeat()
        {
            if (isLobby)
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
            if (isActive && isLobby)
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

        private IEnumerator NATPunch(IPEndPoint endPoint)
        {
            for (int i = 0; i < 10; i++)
            {
                punchClient.Send(new byte[] { 1 }, 1, endPoint);
                yield return new WaitForSeconds(0.25f);
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

            if (!isLobby)
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
            buffers.WriteBool(ref position, puncher != null);

            if (puncher == null)
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
                puncher.ClientSend(segment, channel);
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

            if (puncher != null)
            {
                puncher.ClientDisconnect();
            }
        }

        public override void StartServer()
        {
            if (!isLobby)
            {
                Debug.Log("没有连接到大厅!");
                return;
            }

            if (isClient || isServer)
            {
                Debug.Log("客户端或服务器已经连接!");
                return;
            }

            index = 1;
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
            if (isPunch && puncher != null && clientIp != null)
            {
                buffers.WriteString(ref position, clientIp);
                puncher.StartServer(isPunch ? punchEndPoint.Port + 1 : -1);
            }
            else
            {
                buffers.WriteString(ref position, "0.0.0.0");
            }

            if (isPunch)
            {
                buffers.WriteInt(ref position, 0);
            }
            else
            {
                buffers.WriteInt(ref position, puncher == null ? 1 : puncher.port);
            }

            buffers.WriteBool(ref position, isPunch);
            buffers.WriteBool(ref position, puncher != null && clientIp != null);
            transport.ClientSend(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void ServerSend(int clientId, ArraySegment<byte> segment, Channel channel = Channel.Reliable)
        {
            if (puncher != null && connnections.TryGetSecond(clientId, out int connection))
            {
                puncher.ServerSend(connection, segment, channel);
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
                puncher.ServerDisconnect(connection);
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
                puncher.ClientEarlyUpdate();
            }
        }

        public override void ClientAfterUpdate()
        {
            transport.ClientAfterUpdate();

            if (puncher != null)
            {
                puncher.ClientAfterUpdate();
            }
        }

        public override void ServerEarlyUpdate()
        {
            if (puncher != null)
            {
                puncher.ServerEarlyUpdate();
            }
        }

        public override void ServerAfterUpdate()
        {
            if (puncher != null)
            {
                puncher.ServerAfterUpdate();
            }
        }
    }

    public partial class NetworkLobbyTransport
    {
        public void NATServerConnected(int clientId)
        {
            if (isServer)
            {
                connnections.Add(clientId, index);
                OnServerConnected?.Invoke(index);
                index++;
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