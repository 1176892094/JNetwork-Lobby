using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
        public bool isPublic = true;
        public float sendRate = 3;
        public string serverId;
        public string serverKey = "Secret Key";
        public string roomName = "Game Room";
        public string roomData = "Map 1";
        private StateMode state = StateMode.Disconnect;
        private int playerId;
        private bool isClient;
        private bool isServer;
        private bool punching;
        private byte[] buffers;
        private UdpClient punchClient;
        private SocketProxy clientProxy;
        private IPEndPoint punchEndPoint;
        private IPEndPoint serverEndPoint;
        private IPEndPoint remoteEndPoint;
        private readonly HashMap<int, int> clients = new HashMap<int, int>();
        private readonly HashMap<int, int> connections = new HashMap<int, int>();
        private readonly HashMap<IPEndPoint, SocketProxy> proxies = new HashMap<IPEndPoint, SocketProxy>();

        public event Action<List<Room>> OnRoomUpdate;
        public bool isPunch => puncher != null;
        public bool isActive => state != StateMode.Disconnect;

        private void Awake()
        {
            if (transport == puncher)
            {
                Debug.Log("大厅和内网穿透不能使用同一个传输！");
                return;
            }

            transport.OnClientConnect -= OnClientConnect;
            transport.OnClientDisconnect -= OnClientDisconnect;
            transport.OnClientReceive -= OnClientReceive;
            transport.OnClientConnect += OnClientConnect;
            transport.OnClientDisconnect += OnClientDisconnect;
            transport.OnClientReceive += OnClientReceive;
            if (isPunch)
            {
                puncher.OnServerConnect -= NATServerConnect;
                puncher.OnServerReceive -= NATServerReceive;
                puncher.OnServerDisconnect -= NATServerDisconnect;
                puncher.OnClientConnect -= NATClientConnect;
                puncher.OnClientReceive -= NATClientReceive;
                puncher.OnClientDisconnect -= NATClientDisconnect;
                puncher.OnServerConnect += NATServerConnect;
                puncher.OnServerReceive += NATServerReceive;
                puncher.OnServerDisconnect += NATServerDisconnect;
                puncher.OnClientConnect += NATClientConnect;
                puncher.OnClientReceive += NATClientReceive;
                puncher.OnClientDisconnect += NATClientDisconnect;
            }

            InvokeRepeating(nameof(HeartBeat), sendRate, sendRate);
            return;

            void OnClientConnect()
            {
                state = StateMode.Connect;
            }

            void OnClientDisconnect()
            {
                state = StateMode.Disconnect;
            }

            void OnClientReceive(ArraySegment<byte> segment, int channel)
            {
                try
                {
                    OnMessageReceive(segment, channel);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e);
                }
            }
        }

        private void OnDestroy()
        {
            StopLobby();
        }

        public void StartLobby()
        {
            if (isActive)
            {
                Debug.LogWarning("大厅服务器已经连接！");
                return;
            }

            transport.port = port;
            transport.address = address;
            buffers = new byte[transport.MessageSize(Channel.Reliable)];
            transport.StartClient();
        }

        public void StopLobby()
        {
            if (!isActive) return;
            Debug.Log("停止大厅服务器。");
            state = StateMode.Disconnect;
            if (isPunch)
            {
                punching = false;
                puncher.StopClient();
                puncher.StopServer();
                punchClient?.Dispose();
                clientProxy?.Dispose();
                punchClient = null;
                clientProxy = null;
            }

            clients.Clear();
            proxies.Clear();
            isServer = false;
            isClient = false;
            connections.Clear();
            transport.StopClient();
        }

        private void OnMessageReceive(ArraySegment<byte> segment, int channel)
        {
            var data = segment.Array;
            var position = segment.Offset;
            var opcode = (OpCodes)data.ReadByte(ref position);
            if (opcode == OpCodes.Connect)
            {
                position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.Connected);
                buffers.WriteString(ref position, serverKey);
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position), channel);
            }
            else if (opcode == OpCodes.Connected)
            {
                state = StateMode.Connected;
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
                    OnServerConnect?.Invoke(playerId);
                    playerId++;
                }

                if (isClient)
                {
                    OnClientConnect?.Invoke();
                }
            }
            else if (opcode == OpCodes.LeaveRoom)
            {
                if (isClient)
                {
                    isClient = false;
                    OnClientDisconnect?.Invoke();
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
                        OnServerDisconnect?.Invoke(clients.GetFirst(clientId));
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
                            punchEndPoint = new IPEndPoint(IPAddress.Parse(NetworkUtility.GetHostName()), Random.Range(16000, 17000));
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
                        clientProxy = new SocketProxy(punchEndPoint.Port - 1, SendToClient);
                    }

                    puncher.address = "127.0.0.1";
                    puncher.port = (ushort)(punchEndPoint.Port - 1);
                    puncher.StartClient();
                }
            }
        }

        private async void NATPunch(IPEndPoint remoteEndPoint)
        {
            for (int i = 0; i < 10; i++)
            {
                if (punchClient != null)
                {
                    await punchClient.SendAsync(new[] { byte.MaxValue }, 1, remoteEndPoint);
                    await Task.Delay(250);
                }
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
                            proxy.SendToClient(segment);
                        }
                    }
                    else
                    {
                        proxies.Add(endPoint, new SocketProxy(punchEndPoint.Port + 1, SendToServer, endPoint));
                    }
                }

                if (isClient)
                {
                    if (clientProxy != null)
                    {
                        clientProxy.SendToServer(segment);
                    }
                    else
                    {
                        clientProxy = new SocketProxy(punchEndPoint.Port - 1, SendToClient);
                    }
                }
            }

            punchClient.BeginReceive(ReceiveData, punchClient);
        }

        private void SendToServer(IPEndPoint endPoint, byte[] data)
        {
            punchClient.Send(data, data.Length, endPoint);
        }

        private void SendToClient(byte[] data)
        {
            punchClient.Send(data, data.Length, remoteEndPoint);
        }

        private void HeartBeat()
        {
            if (!isActive) return;
            transport.SendToServer(new[] { byte.MaxValue });
            punchClient?.Send(new byte[] { 0 }, 1, serverEndPoint);
            var keys = proxies.Keys.ToList();
            foreach (var key in keys.Where(ip => proxies.GetFirst(ip).interval > 10))
            {
                proxies.GetFirst(key).Dispose();
                proxies.Remove(key);
            }
        }

        public async void UpdateRoom()
        {
            if (state == StateMode.Connected)
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
                OnRoomUpdate?.Invoke(JsonManager.Reader<List<Room>>(json));
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
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
            }
        }
    }

    public partial class NetworkLobbyTransport
    {
        public override void StartClient()
        {
            if (!isActive)
            {
                Debug.Log("没有连接到大厅!");
                OnClientDisconnect?.Invoke();
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
            transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void StartClient(Uri uri)
        {
            if (uri != null)
            {
                address = uri.Host;
            }

            StartClient();
        }

        public override void SendToServer(ArraySegment<byte> segment, int channel = Channel.Reliable)
        {
            if (punching)
            {
                puncher.SendToServer(segment, channel);
            }
            else
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.UpdateData);
                buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                buffers.WriteInt(ref position, 0);
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position), channel);
            }
        }

        public override void StopClient()
        {
            isClient = false;
            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
            transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
            if (isPunch)
            {
                puncher.StopClient();
            }
        }

        public override void StartServer()
        {
            if (!isActive)
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
            connections.Clear();

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
            buffers.WriteInt(ref position, NetworkManager.Instance.connection);
            buffers.WriteBool(ref position, isPublic);
            if (isPunch)
            {
                buffers.WriteString(ref position, punchEndPoint.Address.ToString());
                buffers.WriteInt(ref position, punchEndPoint.Port + 1);
                puncher.port = (ushort)(punchEndPoint.Port + 1);
                puncher.StartServer();
                Debug.Log("NAT服务器地址:" + punchEndPoint.Address + ":" + (punchEndPoint.Port + 1));
            }
            else
            {
                buffers.WriteString(ref position, "0.0.0.0");
                buffers.WriteInt(ref position, 0);
            }


            buffers.WriteBool(ref position, isPunch);
            buffers.WriteBool(ref position, isPunch && NetworkUtility.GetHostName() != "127.0.0.1");
            transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void SendToClient(int clientId, ArraySegment<byte> segment, int channel = Channel.Reliable)
        {
            if (isPunch && connections.TryGetSecond(clientId, out int connection))
            {
                puncher.SendToClient(connection, segment, channel);
            }
            else
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.UpdateData);
                buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                buffers.WriteInt(ref position, clients.GetSecond(clientId));
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position), channel);
            }
        }

        public override void StopClient(int clientId)
        {
            if (clients.TryGetSecond(clientId, out int ownerId))
            {
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.Disconnect);
                buffers.WriteInt(ref position, ownerId);
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
                return;
            }

            if (isPunch && connections.TryGetSecond(clientId, out int connection))
            {
                puncher.StopClient(connection);
            }
        }

        public override void StopServer()
        {
            if (isServer)
            {
                isServer = false;
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
                if (isPunch)
                {
                    puncher.StopServer();
                }

                var keys = proxies.Keys.ToList();
                foreach (var key in keys)
                {
                    proxies.GetFirst(key).Dispose();
                    proxies.Remove(key);
                }
            }
        }

        public override int MessageSize(int channel)
        {
            return transport.MessageSize(channel);
        }

        public override void ClientEarlyUpdate()
        {
            transport.ClientEarlyUpdate();
            if (isPunch)
            {
                puncher.ClientEarlyUpdate();
            }
        }

        public override void ClientAfterUpdate()
        {
            transport.ClientAfterUpdate();
            if (isPunch)
            {
                puncher.ClientAfterUpdate();
            }
        }

        public override void ServerEarlyUpdate()
        {
            if (isPunch)
            {
                puncher.ServerEarlyUpdate();
            }
        }

        public override void ServerAfterUpdate()
        {
            if (isPunch)
            {
                puncher.ServerAfterUpdate();
            }
        }
    }

    public partial class NetworkLobbyTransport
    {
        public void NATServerConnect(int clientId)
        {
            if (isServer)
            {
                Debug.Log($"客户端 {clientId} 加入NAT服务器");
                connections.Add(clientId, playerId);
                OnServerConnect?.Invoke(playerId);
                playerId++;
            }
        }

        public void NATServerReceive(int clientId, ArraySegment<byte> segment, int channel)
        {
            if (isServer)
            {
                OnServerReceive?.Invoke(connections.GetFirst(clientId), segment, channel);
            }
        }

        public void NATServerDisconnect(int clientId)
        {
            if (isServer)
            {
                Debug.Log($"客户端 {clientId} 从NAT服务器断开");
                OnServerDisconnect?.Invoke(connections.GetFirst(clientId));
                connections.Remove(clientId);
            }
        }

        public void NATClientConnect()
        {
            punching = true;
            OnClientConnect?.Invoke();
        }

        public void NATClientReceive(ArraySegment<byte> segment, int channel)
        {
            if (isClient)
            {
                OnClientReceive?.Invoke(segment, channel);
            }
        }

        public void NATClientDisconnect()
        {
            if (punching)
            {
                isClient = false;
                punching = false;
                Debug.Log("从NAT服务器断开");
                OnClientDisconnect?.Invoke();
            }
            else
            {
                isClient = true;
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.JoinRoom);
                buffers.WriteString(ref position, transport.address);
                buffers.WriteBool(ref position, false);
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
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
            Connect = 1,
            Connected = 2,
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