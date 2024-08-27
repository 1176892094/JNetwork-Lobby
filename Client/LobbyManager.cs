using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using JFramework.Interface;
using UnityEngine;
using UnityEngine.Networking;

namespace JFramework.Net
{
    [DefaultExecutionOrder(1001)]
    public partial class LobbyManager : Transport, IEntity
    {
        public static LobbyManager Instance;
        public Transport transport;
        public bool isPublic = true;
        public string roomName;
        public string roomData;
        public string serverId;
        public string serverKey = "Secret Key";
        [Range(1, 10)] public int sendRate = 3;

        private int playerId;
        private bool isClient;
        private bool isServer;
        private byte[] buffers;
        private StateMode state = StateMode.Disconnect;
        private readonly HashMap<int, int> clients = new HashMap<int, int>();

        public event Action<List<Room>> OnRoomUpdate;

        private void Awake()
        {
            Instance = this;
            transport.OnClientConnect -= OnClientConnect;
            transport.OnClientDisconnect -= OnClientDisconnect;
            transport.OnClientReceive -= OnClientReceive;
            transport.OnClientConnect += OnClientConnect;
            transport.OnClientDisconnect += OnClientDisconnect;
            transport.OnClientReceive += OnClientReceive;
            InvokeRepeating(nameof(HeartBeat), sendRate, sendRate);

            void OnClientConnect()
            {
                state = StateMode.Connect;
            }

            void OnClientDisconnect()
            {
                state = StateMode.Disconnect;
            }

            void OnClientReceive(ArraySegment<byte> segment, byte channel)
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
            if (state != StateMode.Disconnect)
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
            if (state == StateMode.Disconnect) return;
            Debug.Log("停止大厅服务器。");
            state = StateMode.Disconnect;
            clients.Clear();
            isServer = false;
            isClient = false;
            transport.StopClient();
        }

        private void OnMessageReceive(ArraySegment<byte> segment, byte channel)
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
        }

        private void HeartBeat()
        {
            if (state == StateMode.Disconnect) return;
            transport.SendToServer(new[] { byte.MaxValue });
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
                OnRoomUpdate?.Invoke(JsonManager.Read<List<Room>>(json));
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

    public partial class LobbyManager
    {
        public override int MessageSize(byte channel)
        {
            return transport.MessageSize(channel);
        }

        public override void SendToClient(int clientId, ArraySegment<byte> segment, byte channel = Channel.Reliable)
        {
            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.UpdateData);
            buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
            buffers.WriteInt(ref position, clients.GetSecond(clientId));
            transport.SendToServer(new ArraySegment<byte>(buffers, 0, position), channel);
        }

        public override void SendToServer(ArraySegment<byte> segment, byte channel = Channel.Reliable)
        {
            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.UpdateData);
            buffers.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
            buffers.WriteInt(ref position, 0);
            transport.SendToServer(new ArraySegment<byte>(buffers, 0, position), channel);
        }

        public override void StartServer()
        {
            if (state != StateMode.Connected)
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

            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.CreateRoom);
            buffers.WriteString(ref position, roomName);
            buffers.WriteString(ref position, roomData);
            buffers.WriteInt(ref position, NetworkManager.Instance.connection);
            buffers.WriteBool(ref position, isPublic);
            buffers.WriteString(ref position, "0.0.0.0");
            buffers.WriteInt(ref position, 0);


            buffers.WriteBool(ref position, false);
            buffers.WriteBool(ref position, false);
            transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void StopServer()
        {
            if (isServer)
            {
                isServer = false;
                var position = 0;
                buffers.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
                transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
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
            }
        }

        public override void StartClient()
        {
            if (state != StateMode.Connected)
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

            isClient = true;
            int position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.JoinRoom);
            buffers.WriteString(ref position, transport.address);
            buffers.WriteBool(ref position, false);
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

        public override void StopClient()
        {
            isClient = false;
            var position = 0;
            buffers.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
            transport.SendToServer(new ArraySegment<byte>(buffers, 0, position));
        }

        public override void ClientEarlyUpdate()
        {
            transport.ClientEarlyUpdate();
        }

        public override void ClientAfterUpdate()
        {
            transport.ClientAfterUpdate();
        }

        public override void ServerEarlyUpdate()
        {
        }

        public override void ServerAfterUpdate()
        {
        }
    }

    public partial class LobbyManager
    {
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