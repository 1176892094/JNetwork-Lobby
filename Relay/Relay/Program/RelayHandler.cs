using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace JFramework.Net
{
    public partial class RelayHandler
    {
        private const string CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public readonly List<Room> rooms = new List<Room>();
        private readonly int maxPacketSize;
        private readonly Random random = new Random();
        private readonly ArrayPool<byte> sendBuffers;
        private readonly List<int> clientList = new List<int>();
        private readonly Dictionary<int, Room> clientRooms = new Dictionary<int, Room>();
        private readonly Dictionary<string, Room> cachedRooms = new Dictionary<string, Room>();
    }

    public partial class RelayHandler
    {
        public RelayHandler(int maxPacketSize)
        {
            this.maxPacketSize = maxPacketSize;
            sendBuffers = ArrayPool<byte>.Create(maxPacketSize, 50);
        }

        private string GetRoomId()
        {
            string id;
            do
            {
                id = new string(Enumerable.Repeat(CHARS, Program.setting.RandomIdLength).Select(s => s[random.Next(s.Length)]).ToArray());
            } while (cachedRooms.ContainsKey(id));

            return id;
        }

        private string GetServerId()
        {
            if (!Program.setting.UseLoadBalancer)
            {
                return GetRoomId();
            }

            var uri = new Uri($"http://{Program.setting.LoadBalancerAddress}:{Program.setting.LoadBalancerPort}/api/get/id");
            var id = Program.webClient.DownloadString(uri).Replace("\\r", "").Replace("\\n", "").Trim();
            return id;
        }

        private void ProcessData(int clientId, byte[] data, Channel channel, int sendTo)
        {
            var room = clientRooms[clientId];

            if (room != null)
            {
                if (room.hostId == clientId)
                {
                    if (room.clients.Contains(sendTo))
                    {
                        SendRoomClientRpc(clientId, data, channel, sendTo);
                    }
                }
                else
                {
                    SendRoomServerRpc(clientId, data, channel, room);
                }
            }
        }

        private void SendRoomServerRpc(int clientId, byte[] data, Channel channel, Room room)
        {
            if (data.Length > maxPacketSize)
            {
                Program.WriteLogMessage($"客户端 {clientId} 试图发送超过最大数据包大小!", ConsoleColor.Red);
                Program.transport.ServerDisconnect(clientId);
                return;
            }

            var position = 0;
            var buffer = sendBuffers.Rent(maxPacketSize);
            buffer.Write(ref position, (byte)OpCode.GetData);
            buffer.Write(ref position, data);
            buffer.Write(ref position, clientId);
            Program.transport.ServerSend(room.hostId, new ArraySegment<byte>(buffer, 0, position), channel);
            sendBuffers.Return(buffer);
        }
        
        private void SendRoomClientRpc(int clientId, byte[] data, Channel channel, int sendTo)
        {
            if (data.Length > maxPacketSize)
            {
                Program.WriteLogMessage($"客户端 {clientId} 试图发送超过最大数据包大小!", ConsoleColor.Red);
                Program.transport.ServerDisconnect(clientId);
                return;
            }

            var position = 0;
            var buffer = sendBuffers.Rent(maxPacketSize);
            buffer.Write(ref position, (byte)OpCode.GetData);
            buffer.Write(ref position, data);
            Program.transport.ServerSend(sendTo, new ArraySegment<byte>(buffer, 0, position), channel);
            sendBuffers.Return(buffer);
        }

        private void SendClientID(int clientId)
        {
            var position = 0;
            var buffer = sendBuffers.Rent(5);
            buffer.Write(ref position, (byte)OpCode.GetID);
            buffer.Write(ref position, clientId);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            sendBuffers.Return(buffer);
        }
    }

    public partial class RelayHandler
    {
        public void ClientConnected(int clientId)
        {
            clientList.Add(clientId);
            var buffer = sendBuffers.Rent(1);
            var position = 0;
            buffer.Write(ref position, (byte)OpCode.AuthenticationRequest);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            sendBuffers.Return(buffer);
        }

        public void HandleMessage(int clientId, ArraySegment<byte> segment, Channel channel)
        {
            try
            {
                var data = segment.Array;
                var position = segment.Offset;
                data.Read(ref position, out byte b);
                var opcode = (OpCode)b;

                if (opcode == OpCode.AuthenticationResponse)
                {
                    if (clientList.Contains(clientId))
                    {
                        data.Read(ref position, out string authResponse);
                        if (authResponse == Program.setting.AuthenticationKey)
                        {
                            clientList.Remove(clientId);
                            int writePos = 0;
                            var sendBuffer = sendBuffers.Rent(1);
                            sendBuffer.Write(ref writePos, (byte)OpCode.Authenticated);
                            Program.transport.ServerSend(clientId, new ArraySegment<byte>(sendBuffer, 0, writePos));

                            sendBuffers.Return(sendBuffer);
                        }
                        else
                        {
                            Program.WriteLogMessage($"Client {clientId} sent wrong auth key! Removing from RELAY node.");
                            Program.transport.ServerDisconnect(clientId);
                        }
                    }
                }
                else if (opcode == OpCode.CreateRoom)
                {
                    data.Read(ref position, out int players);
                    data.Read(ref position, out string serverName);
                    data.Read(ref position, out bool isPublic);
                    data.Read(ref position, out string serverData);
                    data.Read(ref position, out bool useDirect);
                    data.Read(ref position, out string hostIp);
                    data.Read(ref position, out bool usePunch);
                    data.Read(ref position, out int port);
                    data.Read(ref position, out int appId);
                    data.Read(ref position, out string version);
                    CreateRoom(clientId, players, serverName, isPublic, serverData, useDirect, hostIp, usePunch, port, appId, version);
                }
                else if (opcode == OpCode.RequestID)
                {
                    SendClientID(clientId);
                }
                else if (opcode == OpCode.LeaveRoom)
                {
                    LeaveRoom(clientId);
                }
                else if (opcode == OpCode.JoinServer)
                {
                    data.Read(ref position, out string serverId);
                    data.Read(ref position, out bool useDirectConnect);
                    data.Read(ref position, out string localIp);
                    JoinRoom(clientId, serverId, useDirectConnect, localIp);
                }
                else if (opcode == OpCode.KickPlayer)
                {
                    data.Read(ref position, out int client);
                    LeaveRoom(client, clientId);
                }
                else if (opcode == OpCode.SendData)
                {
                    data.Read(ref position, out byte[] process);
                    data.Read(ref position, out int sendTo);
                    ProcessData(clientId, process, channel, sendTo);
                }
                else if (opcode == OpCode.UpdateRoomData)
                {
                    var playRoom = clientRooms[clientId];
                    if (playRoom == null)
                    {
                        return;
                    }

                    if (playRoom.hostId != clientId)
                    {
                        return;
                    }

                    data.Read(ref position, out bool isActive);
                    if (isActive)
                    {
                        data.Read(ref position, out string serverName);
                        playRoom.serverName = serverName;
                    }

                    data.Read(ref position, out isActive);
                    if (isActive)
                    {
                        data.Read(ref position, out string serverData);
                        playRoom.serverData = serverData;
                    }

                    data.Read(ref position, out isActive);
                    if (isActive)
                    {
                        data.Read(ref position, out bool isPublic);
                        playRoom.isPublic = isPublic;
                    }

                    data.Read(ref position, out isActive);
                    if (isActive)
                    {
                        data.Read(ref position, out int maxPlayers);
                        playRoom.maxPlayers = maxPlayers;
                    }

                    Peer.RoomsModified();
                }
            }
            catch
            {
                Program.WriteLogMessage($"客户端 {clientId} 发送了错误数据!");
                Program.transport.ServerDisconnect(clientId);
            }
        }

        public void HandleDisconnect(int clientId)
        {
            LeaveRoom(clientId);
        }
    }

    public partial class RelayHandler
    {
        private void CreateRoom(int clientId, int maxPlayers, string serverName, bool isPublic, string serverData, bool useDirectConnect, string hostLocalIp, bool useNatPunch, int port, int appId, string version)
        {
            LeaveRoom(clientId);
            Program.instance.NATConnections.TryGetValue(clientId, out IPEndPoint hostIp);

            var room = new Room()
            {
                hostId = clientId,
                maxPlayers = maxPlayers,
                serverName = serverName,
                isPublic = isPublic,
                serverData = serverData,
                appId = appId,
                version = version,
                clients = new List<int>(),
                serverId = GetServerId(),
                hostIp = hostIp,
                hostLocalIp = hostLocalIp,
                supportsDirectConnect = hostIp != null && useDirectConnect,
                port = port,
                useNATPunch = useNatPunch,
                relayInfo = new RelayAddress
                {
                    address = Program.address,
                    port = Program.setting.TransportPort,
                    endpointPort = Program.setting.RelayPort,
                    serverRegion = Program.setting.LoadBalancerRegion
                }
            };

            rooms.Add(room);
            clientRooms.Add(clientId, room);
            cachedRooms.Add(room.serverId, room);

            var position = 0;
            var buffer = sendBuffers.Rent(5);
            buffer.Write(ref position, (byte)OpCode.RoomCreated);
            buffer.Write(ref position, room.serverId);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            sendBuffers.Return(buffer);
            Peer.RoomsModified();
        }

        private void JoinRoom(int clientId, string serverId, bool canDirectConnect, string localIP)
        {
            LeaveRoom(clientId);

            if (cachedRooms.TryGetValue(serverId, out var room))
            {
                if (room.clients.Count < room.maxPlayers)
                {
                    room.clients.Add(clientId);
                    clientRooms.Add(clientId, room);

                    int sendJoinPos = 0;
                    byte[] sendJoinBuffer = sendBuffers.Rent(500);

                    if (canDirectConnect && Program.instance.NATConnections.ContainsKey(clientId) && room.supportsDirectConnect)
                    {
                        sendJoinBuffer.Write(ref sendJoinPos, (byte)OpCode.DirectConnectIP);

                        if (Program.instance.NATConnections[clientId].Address.Equals(room.hostIp.Address))
                            sendJoinBuffer.Write(ref sendJoinPos, room.hostLocalIp == localIP ? "127.0.0.1" : room.hostLocalIp);
                        else
                            sendJoinBuffer.Write(ref sendJoinPos, room.hostIp.Address.ToString());

                        sendJoinBuffer.Write(ref sendJoinPos, room.useNATPunch ? room.hostIp.Port : room.port);
                        sendJoinBuffer.Write(ref sendJoinPos, room.useNATPunch);

                        Program.transport.ServerSend(clientId, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));

                        if (room.useNATPunch)
                        {
                            sendJoinPos = 0;
                            sendJoinBuffer.Write(ref sendJoinPos, (byte)OpCode.DirectConnectIP);

                            sendJoinBuffer.Write(ref sendJoinPos, Program.instance.NATConnections[clientId].Address.ToString());
                            sendJoinBuffer.Write(ref sendJoinPos, Program.instance.NATConnections[clientId].Port);
                            sendJoinBuffer.Write(ref sendJoinPos, true);

                            Program.transport.ServerSend(room.hostId, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));
                        }

                        sendBuffers.Return(sendJoinBuffer);

                        Peer.RoomsModified();
                        return;
                    }

                    sendJoinBuffer.Write(ref sendJoinPos, (byte)OpCode.ServerJoined);
                    sendJoinBuffer.Write(ref sendJoinPos, clientId);

                    Program.transport.ServerSend(clientId, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));
                    Program.transport.ServerSend(room.hostId, new ArraySegment<byte>(sendJoinBuffer, 0, sendJoinPos));
                    sendBuffers.Return(sendJoinBuffer);

                    Peer.RoomsModified();
                    return;
                }
            }

            var position = 0;
            var buffer = sendBuffers.Rent(1);

            buffer.Write(ref position, (byte)OpCode.ServerLeft);

            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            sendBuffers.Return(buffer);
        }

        private void LeaveRoom(int clientId, int requiredHostId = -1)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                if (rooms[i].hostId == clientId)
                {
                    var position = 0;
                    var buffer = sendBuffers.Rent(1);
                    buffer.Write(ref position, (byte)OpCode.ServerLeft);
                    foreach (var client in rooms[i].clients)
                    {
                        Program.transport.ServerSend(client, new ArraySegment<byte>(buffer, 0, position));
                        clientRooms.Remove(client);
                    }

                    sendBuffers.Return(buffer);
                    rooms[i].clients.Clear();
                    cachedRooms.Remove(rooms[i].serverId);
                    rooms.RemoveAt(i);
                    clientRooms.Remove(clientId);
                    Peer.RoomsModified();
                    return;
                }

                if (requiredHostId != -1 && rooms[i].hostId != requiredHostId)
                {
                    continue;
                }

                if (rooms[i].clients.RemoveAll(x => x == clientId) > 0)
                {
                    var position = 0;
                    var buffer = sendBuffers.Rent(5);

                    buffer.Write(ref position, (byte)OpCode.PlayerDisconnected);
                    buffer.Write(ref position, clientId);

                    Program.transport.ServerSend(rooms[i].hostId, new ArraySegment<byte>(buffer, 0, position));
                    sendBuffers.Return(buffer);

                    position = 0;
                    buffer = sendBuffers.Rent(1);
                    buffer.Write(ref position, (byte)OpCode.ServerLeft);
                    Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                    sendBuffers.Return(buffer);

                    Peer.RoomsModified();
                    clientRooms.Remove(clientId);
                }
            }
        }
    }
}