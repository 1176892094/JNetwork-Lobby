using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace JFramework.Net
{
    [DefaultExecutionOrder(1001)]
    public partial class NetworkRelay : Transport
    {
        public Transport transport;
        public string serverIp = "34.67.125.123";
        public ushort serverPort = 8080;
        public float heartBeatInterval = 3;
        public bool connectOnAwake = true;
        public string authenticationKey = "Secret Auth Key";
        public UnityEvent diconnectedFromRelay;

        public bool isPunch = true;
        public ushort NATPunchtroughPort = 7776;

        public string serverName = "My awesome server!";
        public string extraServerData = "Map 1";
        public int maxServerPlayers = 10;
        public bool isPublicServer = true;

        public UnityEvent serverListUpdated;
        public List<RelayServerInfo> relayServerList = new List<RelayServerInfo>();
        public int serverId = -1;

        private bool isAuth;
        private bool isInit;
        private bool isRelay;
        private bool isClient;
        private bool isServer;
        private bool isDirect;
        private int currentMemberId;
        private int cachedHostId;
        private byte[] clientBuffer;
        private UdpClient NATPuncher;
        private IPEndPoint NATIP;
        private IPEndPoint relayPunchIp;
        private IPEndPoint directEndPoint;
        private SocketProxy clientProxy;
        private NetworkPunch directPunch;
        private readonly byte[] punchData = { 1 };
        private HashMap<int, int> relayClients = new HashMap<int, int>();
        private HashMap<int, int> directClients = new HashMap<int, int>();
        private readonly HashMap<IPEndPoint, SocketProxy> proxies = new HashMap<IPEndPoint, SocketProxy>();

        private void Awake()
        {
            if (transport is NetworkRelay)
            {
                throw new Exception("需要使用一个不同的传输。");
            }

            directPunch = GetComponent<NetworkPunch>();

            if (directPunch != null)
            {
                if (isPunch && !directPunch.IsPunch())
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

            if (connectOnAwake)
            {
                if (!isRelay)
                {
                    clientBuffer = new byte[transport.GetMaxPacketSize()];
                    transport.address = serverIp;
                    transport.ClientConnect();
                }
                else
                {
                    Debug.Log("已连接到中继服务器!");
                }
            }

            InvokeRepeating(nameof(SendHeartBeat), heartBeatInterval, heartBeatInterval);
        }


        private void ClientConnected()
        {
            isRelay = true;
        }

        private void ClientDisconnected()
        {
            isRelay = false;
            isAuth = false;
            diconnectedFromRelay?.Invoke();
        }


        public override void ClientConnect(Uri uri = null)
        {
            if (uri != null)
            {
                address = uri.Host;
            }

            if (!isRelay || !int.TryParse(address, out cachedHostId))
            {
                Debug.Log("没有连接到中继或无效的服务器id!");
                OnClientDisconnected?.Invoke();
                return;
            }

            if (isClient || isServer)
            {
                throw new Exception("托管时无法连接/已经连接!");
            }

            int position = 0;
            isDirect = false;
            clientBuffer.WriteByte(ref position, (byte)OpCodes.JoinServer);
            clientBuffer.WriteInt(ref position, cachedHostId);
            clientBuffer.WriteBool(ref position, directPunch != null);

            if (GetLocalIp() == null)
            {
                clientBuffer.WriteString(ref position, "0.0.0.0");
            }
            else
            {
                clientBuffer.WriteString(ref position, GetLocalIp());
            }

            isClient = true;
            transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));
        }

        public override int GetMaxPacketSize(Channel channel = Channel.Reliable)
        {
            return transport.GetMaxPacketSize(channel);
        }

        public override int UnreliableSize() => 0;

        public override void ClientEarlyUpdate()
        {
            transport.ClientEarlyUpdate();

            if (directPunch != null)
            {
                directPunch.transport.ClientEarlyUpdate();
            }
        }

        public override void ClientAfterUpdate()
        {
            transport.ClientAfterUpdate();

            if (directPunch != null)
            {
                directPunch.transport.ClientAfterUpdate();
            }
        }

        public override void ServerEarlyUpdate()
        {
            if (directPunch != null)
            {
                directPunch.transport.ServerEarlyUpdate();
            }
        }

        public override void ServerAfterUpdate()
        {
            if (directPunch != null)
            {
                directPunch.transport.ServerAfterUpdate();
            }
        }

        private void ReceiveData(IAsyncResult result)
        {
            var newEndPoint = new IPEndPoint(IPAddress.Any, 0);
            var data = NATPuncher.EndReceive(result, ref newEndPoint);

            if (!newEndPoint.Address.Equals(relayPunchIp.Address))
            {
                if (isServer)
                {
                    if (proxies.TryGetFirst(newEndPoint, out SocketProxy foundProxy))
                    {
                        if (data.Length > 2)
                            foundProxy.RemoteRelayData(data, data.Length);
                    }
                    else
                    {
                        proxies.Add(newEndPoint, new SocketProxy(NATIP.Port + 1, newEndPoint));
                        proxies.GetFirst(newEndPoint).OnReceive += ServerProcessProxyData;
                    }
                }

                if (isClient)
                {
                    if (clientProxy == null)
                    {
                        clientProxy = new SocketProxy(NATIP.Port - 1);
                        clientProxy.OnReceive += ClientProcessProxyData;
                    }
                    else
                    {
                        clientProxy.ClientRelayData(data, data.Length);
                    }
                }
            }

            NATPuncher.BeginReceive(ReceiveData, NATPuncher);
        }

        private void ServerProcessProxyData(IPEndPoint remoteEndpoint, byte[] data)
        {
            NATPuncher.Send(data, data.Length, remoteEndpoint);
        }

        private void ClientProcessProxyData(IPEndPoint _, byte[] data)
        {
            NATPuncher.Send(data, data.Length, directEndPoint);
        }

        private void SendHeartBeat()
        {
            if (isRelay)
            {
                int pos = 0;
                clientBuffer.WriteByte(ref pos, 200);
                transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, pos));

                if (NATPuncher != null)
                    NATPuncher.Send(new byte[] { 0 }, 1, relayPunchIp);

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
            if (isAuth && isRelay)
            {
                GetServerList();
            }
            else
            {
                Debug.Log("您必须连接到中继以请求服务器列表!");
            }
        }

        IEnumerator NATPunch(IPEndPoint remoteAddress)
        {
            for (int i = 0; i < 10; i++)
            {
                NATPuncher.Send(punchData, 1, remoteAddress);
                yield return new WaitForSeconds(0.25f);
            }
        }

        void ClientReceive(ArraySegment<byte> segment, Channel channel)
        {
            try
            {
                var data = segment.Array;
                var position = segment.Offset;
                var opcode = (OpCodes)data.ReadByte(ref position);
                if (opcode == OpCodes.Authenticated)
                {
                    isAuth = true;
                    RequestServerList();
                }
                else if (opcode == OpCodes.AuthenticationRequest)
                {
                    position = 0;
                    clientBuffer.WriteByte(ref position, (byte)OpCodes.AuthenticationResponse);
                    clientBuffer.WriteString(ref position, authenticationKey);
                    transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));
                }
                else if (opcode == OpCodes.GetData)
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
                else if (opcode == OpCodes.ServerLeft)
                {
                    if (isClient)
                    {
                        isClient = false;
                        OnClientDisconnected?.Invoke();
                    }
                }
                else if (opcode == OpCodes.PlayerDisconnected)
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
                else if (opcode == OpCodes.RoomCreated)
                {
                    serverId = data.ReadInt(ref position);
                }
                else if (opcode == OpCodes.ServerJoined)
                {
                    int clientId = data.ReadInt(ref position);
                    if (isClient)
                    {
                        OnClientConnected?.Invoke();
                    }

                    if (isServer)
                    {
                        relayClients.Add(clientId, currentMemberId);
                        OnServerConnected?.Invoke(currentMemberId);
                        currentMemberId++;
                    }
                }
                else if (opcode == OpCodes.DirectConnectIP)
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
                        if (clientProxy == null && isPunch && attemptNatPunch)
                        {
                            clientProxy = new SocketProxy(NATIP.Port - 1);
                            clientProxy.OnReceive += ClientProcessProxyData;
                        }

                        if (isPunch && attemptNatPunch)
                        {
                            directPunch.JoinServer("127.0.0.1", NATIP.Port - 1);
                        }
                        else
                        {
                            directPunch.JoinServer(directIp, directPort);
                        }
                    }
                }
                else if (opcode == OpCodes.RequestNATConnection)
                {
                    if (GetLocalIp() != null && directPunch != null)
                    {
                        NATPuncher = new UdpClient { ExclusiveAddressUse = false };
                        NATIP = new IPEndPoint(IPAddress.Parse(GetLocalIp()), UnityEngine.Random.Range(16000, 17000));
                        NATPuncher.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        NATPuncher.Client.Bind(NATIP);
                        relayPunchIp = new IPEndPoint(IPAddress.Parse(serverIp), NATPunchtroughPort);

                        var buffer = new byte[150];
                        position = 0;
                        buffer.WriteBool(ref position, true);
                        buffer.WriteString(ref position, data.ReadString(ref position));
                        NATPuncher.Send(buffer, position, relayPunchIp);
                        NATPuncher.Send(buffer, position, relayPunchIp);
                        NATPuncher.Send(buffer, position, relayPunchIp);
                        NATPuncher.BeginReceive(ReceiveData, NATPuncher);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(e.ToString());
            }
        }

        private async void GetServerList()
        {
            var uri = $"http://{serverIp}:{serverPort}/api/compressed/servers";
            using var request = UnityWebRequest.Get(uri);
            await request.SendWebRequest();
            var result = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("LRM | Network Error while retreiving the server list!");
                return;
            }

            relayServerList?.Clear();
            relayServerList = JsonUtility.FromJson<Variables<RelayServerInfo>>(result.Decompress()).value;
            serverListUpdated?.Invoke();
        }

        public void UpdateRoomInfo(string newServerName = null, string newServerData = null, bool? isPublic = null, int? newPlayerCap = null)
        {
            if (isServer)
            {
                int position = 0;
                clientBuffer.WriteByte(ref position, (byte)OpCodes.UpdateRoomData);
                if (!string.IsNullOrEmpty(newServerName))
                {
                    clientBuffer.WriteBool(ref position, true);
                    clientBuffer.WriteString(ref position, newServerName);
                }
                else
                {
                    clientBuffer.WriteBool(ref position, false);
                }

                if (!string.IsNullOrEmpty(newServerData))
                {
                    clientBuffer.WriteBool(ref position, true);
                    clientBuffer.WriteString(ref position, newServerData);
                }
                else
                {
                    clientBuffer.WriteBool(ref position, false);
                }

                if (isPublic != null)
                {
                    clientBuffer.WriteBool(ref position, true);
                    clientBuffer.WriteBool(ref position, isPublic.Value);
                }
                else
                {
                    clientBuffer.WriteBool(ref position, false);
                }

                if (newPlayerCap != null)
                {
                    clientBuffer.WriteBool(ref position, true);
                    clientBuffer.WriteInt(ref position, newPlayerCap.Value);
                }
                else
                {
                    clientBuffer.WriteBool(ref position, false);
                }

                transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));
            }
        }


        public override void ClientDisconnect()
        {
            isClient = false;
            var position = 0;
            clientBuffer.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
            transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));

            if (directPunch != null)
            {
                directPunch.ClientDisconnect();
            }
        }

        public override void ClientSend(ArraySegment<byte> segment, Channel channel = Channel.Reliable)
        {
            if (isDirect)
            {
                directPunch.ClientSend(segment, channel);
            }
            else
            {
                var position = 0;
                clientBuffer.WriteByte(ref position, (byte)OpCodes.SendData);
                clientBuffer.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                clientBuffer.WriteInt(ref position, 0);
                transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));
            }
        }

        public override void ServerDisconnect(int clientId)
        {
            if (relayClients.TryGetSecond(clientId, out int relayId))
            {
                var position = 0;
                clientBuffer.WriteByte(ref position, (byte)OpCodes.KickPlayer);
                clientBuffer.WriteInt(ref position, relayId);
            }

            if (directClients.TryGetSecond(clientId, out int directId))
            {
                directPunch.KickClient(directId);
            }
        }

        public override void ServerSend(int clientId, ArraySegment<byte> segment, Channel channel = Channel.Reliable)
        {
            if (directPunch != null && directClients.TryGetSecond(clientId, out int directId))
            {
                directPunch.ServerSend(directId, segment, channel);
            }
            else
            {
                var position = 0;
                clientBuffer.WriteByte(ref position, (byte)OpCodes.SendData);
                clientBuffer.WriteBytes(ref position, segment.Array.Take(segment.Count).ToArray());
                clientBuffer.WriteInt(ref position, relayClients.GetSecond(clientId));
                transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position), channel);
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
            currentMemberId = 1;
            relayClients = new HashMap<int, int>();
            directClients = new HashMap<int, int>();

            var keys = new List<IPEndPoint>(proxies.Keys);
            foreach (var key in keys)
            {
                proxies.GetFirst(key).Dispose();
                proxies.Remove(key);
            }

            int position = 0;
            clientBuffer.WriteByte(ref position, (byte)OpCodes.CreateRoom);
            clientBuffer.WriteInt(ref position, maxServerPlayers);
            clientBuffer.WriteString(ref position, serverName);
            clientBuffer.WriteBool(ref position, isPublicServer);
            clientBuffer.WriteString(ref position, extraServerData);
            clientBuffer.WriteBool(ref position, directPunch != null && GetLocalIp() != null);

            if (directPunch != null && GetLocalIp() != null)
            {
                clientBuffer.WriteString(ref position, GetLocalIp());
                directPunch.StartServer(isPunch ? NATIP.Port + 1 : -1);
            }
            else
            {
                clientBuffer.WriteString(ref position, "0.0.0.0");
            }

            if (isPunch)
            {
                clientBuffer.WriteBool(ref position, true);
                clientBuffer.WriteInt(ref position, 0);
            }
            else
            {
                clientBuffer.WriteBool(ref position, false);
                clientBuffer.WriteInt(ref position, directPunch == null ? 1 : directPunch.IsPunch() ? directPunch.GetTransportPort() : 1);
            }

            transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));
        }

        public override void StopServer()
        {
            if (isServer)
            {
                isServer = false;
                var position = 0;
                clientBuffer.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
                transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));

                if (directPunch != null)
                {
                    directPunch.StopServer();
                }

                var keys = new List<IPEndPoint>(proxies.Keys);
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
                Scheme = "LRM",
                Host = serverId.ToString()
            };

            return builder.Uri;
        }

        private static string GetLocalIp()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
    }

    [Serializable]
    public struct RelayServerInfo
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

    public partial class NetworkRelay
    {
        public void DirectAddClient(int clientId)
        {
            if (isServer)
            {
                directClients.Add(clientId, currentMemberId);
                OnServerConnected?.Invoke(currentMemberId);
                currentMemberId++;
            }
        }

        public void DirectRemoveClient(int clientId)
        {
            if (!isServer)
            {
                OnServerDisconnected?.Invoke(directClients.GetFirst(clientId));
                directClients.Remove(clientId);
            }
        }

        public void DirectReceiveData(ArraySegment<byte> data, Channel channel, int clientId = -1)
        {
            if (isServer)
            {
                OnServerReceive?.Invoke(directClients.GetFirst(clientId), data, channel);
            }

            if (isClient)
            {
                OnClientReceive?.Invoke(data, channel);
            }
        }

        public void DirectClientConnected()
        {
            isDirect = true;
            OnClientConnected?.Invoke();
        }

        public void DirectDisconnected()
        {
            if (isDirect)
            {
                isClient = false;
                isDirect = false;
                OnClientDisconnected?.Invoke();
            }
            else
            {
                var position = 0;
                isClient = true;
                isDirect = false;
                clientBuffer.WriteByte(ref position, (byte)OpCodes.JoinServer);
                clientBuffer.WriteInt(ref position, cachedHostId);
                clientBuffer.WriteBool(ref position, false);
                transport.ClientSend(new ArraySegment<byte>(clientBuffer, 0, position));
            }

            if (clientProxy != null)
            {
                clientProxy.Dispose();
                clientProxy = null;
            }
        }
    }
}