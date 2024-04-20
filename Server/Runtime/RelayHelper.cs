using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace JFramework.Net
{
    public class RelayHelper
    {
        private readonly int maxPacketSize;
        private readonly ArrayPool<byte> buffers;
        private readonly List<int> pendingAuthentication = new List<int>();
        public readonly Dictionary<int, Room> clients = new Dictionary<int, Room>();
        public readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();

        public RelayHelper(int maxPacketSize)
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
                var key = data.ReadByte(ref position);
                var opcode = (OpCodes)key;
                if (key < 200)
                {
                    Console.WriteLine(opcode);
                }

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
                        CreateRoom(clientId, data.ReadInt(ref position), data.ReadString(ref position), data.ReadBool(ref position),
                            data.ReadString(ref position), data.ReadBool(ref position), data.ReadString(ref position),
                            data.ReadBool(ref position), data.ReadInt(ref position));
                        break;
                    case OpCodes.RequestId:
                        SendClientId(clientId);
                        break;
                    case OpCodes.LeaveRoom:
                        LeaveRoom(clientId);
                        break;
                    case OpCodes.JoinServer:
                        JoinRoom(clientId, data.ReadString(ref position), data.ReadBool(ref position), data.ReadString(ref position));
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
                if (room.clientId == clientId)
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
                    Program.transport.ServerSend(room.clientId, new ArraySegment<byte>(buffer, 0, position), channel);
                    buffers.Return(buffer);
                }
            }
        }

        private void JoinRoom(int clientId, string serverId, bool puncher, string address)
        {
            LeaveRoom(clientId);
            if (rooms.TryGetValue(serverId, out var room) && room.clients.Count < room.maxPlayers)
            {
                room.clients.Add(clientId);
                var position = 0;
                var buffer = buffers.Rent(500);
                if (puncher && Program.instance.connections.TryGetValue(clientId,out var connection))
                {
                    buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                    if (connection.Address.Equals(room.proxy.Address))
                    {
                        buffer.WriteString(ref position, room.address == address ? "127.0.0.1" : room.address);
                    }
                    else
                    {
                        buffer.WriteString(ref position, room.proxy.Address.ToString());
                    }

                    buffer.WriteInt(ref position, room.isPunch ? room.proxy.Port : room.port);
                    buffer.WriteBool(ref position, room.isPunch);
                    Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                    if (room.isPunch)
                    {
                        position = 0;
                        buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                        buffer.WriteString(ref position, connection.Address.ToString());
                        buffer.WriteInt(ref position, connection.Port);
                        buffer.WriteBool(ref position, true);
                        Program.transport.ServerSend(room.clientId, new ArraySegment<byte>(buffer, 0, position));
                    }

                    buffers.Return(buffer);
                    return;
                }

                buffer.WriteByte(ref position, (byte)OpCodes.JoinServerAfter);
                buffer.WriteInt(ref position, clientId);
                Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                Program.transport.ServerSend(room.clientId, new ArraySegment<byte>(buffer, 0, position));
                buffers.Return(buffer);
            }
            else
            {
                var position = 0;
                var buffer = buffers.Rent(1);
                buffer.WriteByte(ref position, (byte)OpCodes.LeaveServer);
                Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                buffers.Return(buffer);
            }
        }

        private void CreateRoom(int clientId, int maxPlayers, string serverName, bool isPublic, string serverData, bool useDirect, string hostLocalIP, bool isPunch, int port)
        {
            LeaveRoom(clientId);
            Program.instance.connections.TryGetValue(clientId, out var hostIP);
            var room = new Room
            {
                clientId = clientId,
                maxPlayers = maxPlayers,
                serverName = serverName,
                isPublic = isPublic,
                serverData = serverData,
                clients = new List<int>(),
                serverId = GetServerId(),
                proxy = hostIP,
                address = hostLocalIP,
                isDirect = hostIP != null && useDirect,
                port = port,
                isPunch = isPunch
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
                if (room.clientId == clientId)
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

                if (requiredHostId >= 0 && room.clientId != requiredHostId)
                {
                    continue;
                }

                if (room.clients.RemoveAll(x => x == clientId) > 0)
                {
                    var position = 0;
                    var buffer = buffers.Rent(5);
                    buffer.WriteByte(ref position, (byte)OpCodes.Disconnect);
                    buffer.WriteInt(ref position, clientId);
                    Program.transport.ServerSend(room.clientId, new ArraySegment<byte>(buffer, 0, position));
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