using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace JFramework.Net
{
    public class RelayEvent
    {
        private readonly int maxPacketSize;
        private readonly ArrayPool<byte> buffers;
        private readonly List<int> pendingAuthentication = new List<int>();
        public readonly List<Room> rooms = new List<Room>();

        public RelayEvent(int maxPacketSize)
        {
            this.maxPacketSize = maxPacketSize;
            buffers = ArrayPool<byte>.Create(maxPacketSize, 50);
        }

        public void ServerConnected(int clientId)
        {
            pendingAuthentication.Add(clientId);
            var buffer = buffers.Rent(1);
            var position = 0;
            buffer.WriteByte(ref position, (byte)OpCodes.AuthorityRequest);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            buffers.Return(buffer);
        }

        public void ServerReceive(int clientId, ArraySegment<byte> segmentData, Channel channel)
        {
            try
            {
                var data = segmentData.Array;
                var position = segmentData.Offset;
                var opcode = (OpCodes)data.ReadByte(ref position);
                if (pendingAuthentication.Contains(clientId))
                {
                    if (opcode == OpCodes.AuthorityResponse)
                    {
                        var response = data.ReadString(ref position);
                        if (response == Program.setting.AuthenticationKey)
                        {
                            pendingAuthentication.Remove(clientId);
                            position = 0;
                            var buffer = buffers.Rent(1);
                            buffer.WriteByte(ref position, (byte)OpCodes.Authority);
                            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                        }
                    }

                    return;
                }

                switch (opcode)
                {
                    case OpCodes.CreateRoom:
                        CreateRoom(clientId, data.ReadInt(ref position), data.ReadString(ref position), data.ReadBool(ref position), data.ReadString(ref position), data.ReadBool(ref position), data.ReadString(ref position), data.ReadBool(ref position), data.ReadInt(ref position));
                        break;
                    case OpCodes.RequestId:
                        SendClientId(clientId);
                        break;
                    case OpCodes.LeaveRoom:
                        LeaveRoom(clientId);
                        break;
                    case OpCodes.JoinServer:
                        JoinRoom(clientId, data.ReadInt(ref position), data.ReadBool(ref position), data.ReadString(ref position));
                        break;
                    case OpCodes.RemoveClient:
                        LeaveRoom(data.ReadInt(ref position), clientId);
                        break;
                    case OpCodes.SendData:
                        ProcessData(clientId, data.ReadBytes(ref position), channel, data.ReadInt(ref position));
                        break;
                    case OpCodes.UpdateRoom:
                        var playerRoom = GetRoomForPlayer(clientId);
                        if (playerRoom == null) return;
                        if (data.ReadBool(ref position))
                        {
                            playerRoom.serverName = data.ReadString(ref position);
                        }

                        if (data.ReadBool(ref position))
                        {
                            playerRoom.serverData = data.ReadString(ref position);
                        }

                        if (data.ReadBool(ref position))
                        {
                            playerRoom.isPublic = data.ReadBool(ref position);
                        }

                        if (data.ReadBool(ref position))
                        {
                            playerRoom.maxPlayers = data.ReadInt(ref position);
                        }

                        break;
                }
            }
            catch
            {
                // ignored
            }
        }

        public void ServerDisconnected(int clientId)
        {
            LeaveRoom(clientId);
        }

        private void ProcessData(int clientId, byte[] clientData, Channel channel, int sendTo = -1)
        {
            var room = GetRoomForPlayer(clientId);
            if (room != null)
            {
                if (room.hostId == clientId)
                {
                    if (room.clients.Contains(sendTo))
                    {
                        var position = 0;
                        var buffer = buffers.Rent(maxPacketSize);
                        buffer.WriteByte(ref position, (byte)OpCodes.ReceiveData);
                        buffer.WriteBytes(ref position, clientData);
                        Program.transport.ServerSend(sendTo, new ArraySegment<byte>(buffer, 0, position), channel);
                        buffers.Return(buffer);
                    }
                }
                else
                {
                    var position = 0;
                    var buffer = buffers.Rent(maxPacketSize);
                    buffer.WriteByte(ref position, (byte)OpCodes.ReceiveData);
                    buffer.WriteBytes(ref position, clientData);
                    buffer.WriteInt(ref position, clientId);
                    Program.transport.ServerSend(room.hostId, new ArraySegment<byte>(buffer, 0, position), channel);
                    buffers.Return(buffer);
                }
            }
        }

        private Room GetRoomForPlayer(int clientId)
        {
            foreach (var room in rooms)
            {
                if (room.hostId == clientId)
                {
                    return room;
                }

                if (room.clients.Contains(clientId))
                {
                    return room;
                }
            }

            return null;
        }

        private void JoinRoom(int clientId, int serverId, bool isDirect, string localIp)
        {
            LeaveRoom(clientId);
            int position;
            foreach (var room in rooms.Where(room => room.serverId == serverId && room.clients.Count < room.maxPlayers))
            {
                room.clients.Add(clientId);
                position = 0;
                var buffer = buffers.Rent(500);
                if (isDirect && Program.instance.connections.ContainsKey(clientId))
                {
                    buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                    if (Program.instance.connections[clientId].Address.Equals(room.hostIP.Address))
                    {
                        buffer.WriteString(ref position, room.hostLocalIP == localIp ? "127.0.0.1" : room.hostLocalIP);
                    }
                    else
                    {
                        buffer.WriteString(ref position, room.hostIP.Address.ToString());
                    }

                    buffer.WriteInt(ref position, room.useNATPunch ? room.hostIP.Port : room.port);
                    buffer.WriteBool(ref position, room.useNATPunch);
                    Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                    if (room.useNATPunch)
                    {
                        position = 0;
                        buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                        Console.WriteLine(Program.instance.connections[clientId].Address.ToString());
                        buffer.WriteString(ref position, Program.instance.connections[clientId].Address.ToString());
                        buffer.WriteInt(ref position, Program.instance.connections[clientId].Port);
                        buffer.WriteBool(ref position, true);
                        Program.transport.ServerSend(room.hostId, new ArraySegment<byte>(buffer, 0, position));
                    }

                    buffers.Return(buffer);
                    return;
                }

                buffer.WriteByte(ref position, (byte)OpCodes.JoinServerAfter);
                buffer.WriteInt(ref position, clientId);
                Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                Program.transport.ServerSend(room.hostId, new ArraySegment<byte>(buffer, 0, position));
                buffers.Return(buffer);
                return;
            }

            position = 0;
            var sendBuffer = buffers.Rent(1);
            sendBuffer.WriteByte(ref position, (byte)OpCodes.LeaveServer);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(sendBuffer, 0, position));
            buffers.Return(sendBuffer);
        }

        private void CreateRoom(int clientId, int maxPlayers, string serverName, bool isPublic, string serverData, bool useDirect, string hostLocalIP, bool isPunch, int port)
        {
            LeaveRoom(clientId);
            Program.instance.connections.TryGetValue(clientId, out var hostIP);
            var room = new Room
            {
                hostId = clientId,
                maxPlayers = maxPlayers,
                serverName = serverName,
                isPublic = isPublic,
                serverData = serverData,
                clients = new List<int>(),
                serverId = GetServerId(),
                hostIP = hostIP,
                hostLocalIP = hostLocalIP,
                supportsDirectConnect = hostIP != null && useDirect,
                port = port,
                useNATPunch = isPunch
            };

            rooms.Add(room);
            var position = 0;
            var buffer = buffers.Rent(5);
            buffer.WriteByte(ref position, (byte)OpCodes.CreateRoomAfter);
            buffer.WriteInt(ref position, clientId);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            buffers.Return(buffer);
        }

        private void LeaveRoom(int clientId, int requiredHostId = -1)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room.hostId == clientId)
                {
                    var position = 0;
                    var buffer = buffers.Rent(1);
                    buffer.WriteByte(ref position, (byte)OpCodes.LeaveServer);
                    foreach (var id in room.clients)
                    {
                        Program.transport.ServerSend(id, new ArraySegment<byte>(buffer, 0, position));
                    }

                    buffers.Return(buffer);
                    room.clients.Clear();
                    rooms.RemoveAt(i);
                    return;
                }

                if (requiredHostId >= 0 && room.hostId != requiredHostId)
                {
                    continue;
                }

                if (room.clients.RemoveAll(x => x == clientId) > 0)
                {
                    var position = 0;
                    var buffer = buffers.Rent(5);
                    buffer.WriteByte(ref position, (byte)OpCodes.Disconnect);
                    buffer.WriteInt(ref position, clientId);
                    Program.transport.ServerSend(room.hostId, new ArraySegment<byte>(buffer, 0, position));
                    buffers.Return(buffer);
                }
            }
        }
        
        private int GetServerId()
        {
            var random = new Random();
            var rand = random.Next(int.MinValue, int.MaxValue);
            while (rooms.Any(room => room.serverId == rand))
            {
                rand = random.Next(int.MinValue, int.MaxValue);
            }

            return rand;
        }

        private void SendClientId(int clientId)
        {
            var position = 0;
            var buffer = buffers.Rent(5);
            buffer.WriteByte(ref position, (byte)OpCodes.ResponseId);
            buffer.WriteInt(ref position, clientId);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            buffers.Return(buffer);
        }
    }
}