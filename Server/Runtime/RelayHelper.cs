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
        private readonly List<int> connections = new List<int>();
        public readonly Dictionary<int, Room> clients = new Dictionary<int, Room>();
        public readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();

        public RelayHelper(int maxPacketSize)
        {
            this.maxPacketSize = maxPacketSize;
            buffers = ArrayPool<byte>.Create(maxPacketSize, 50);
        }

        public void ServerConnected(int clientId)
        {
            connections.Add(clientId);
            var buffer = buffers.Rent(1);
            var position = 0;
            buffer.WriteByte(ref position, (byte)OpCodes.Connected);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            buffers.Return(buffer);
        }

        public void ServerReceive(int clientId, ArraySegment<byte> segment, Channel channel)
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

                if (connections.Contains(clientId))
                {
                    if (opcode == OpCodes.Authority)
                    {
                        var response = data.ReadString(ref position);
                        if (response == Program.setting.ServerKey)
                        {
                            connections.Remove(clientId);
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
                    case OpCodes.LeaveRoom:
                        LeaveRoom(clientId);
                        break;
                    case OpCodes.JoinRoom:
                        JoinRoom(clientId, data.ReadString(ref position), data.ReadBool(ref position), data.ReadString(ref position));
                        break;
                    case OpCodes.Disconnect:
                        LeaveRoom(data.ReadInt(ref position), clientId);
                        break;
                    case OpCodes.UpdateData:
                        UpdateData(clientId, data.ReadBytes(ref position), channel, data.ReadInt(ref position));
                        break;
                    case OpCodes.UpdateRoom:
                        if (clients.TryGetValue(clientId, out var room))
                        {
                            room.name = data.ReadString(ref position) ?? "Room";
                            room.data = data.ReadString(ref position) ?? "1";
                            room.active = data.ReadBool(ref position);
                            room.count = data.ReadInt(ref position);
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

        private void UpdateData(int clientId, byte[] segment, Channel channel, int targetId)
        {
            if (clients.TryGetValue(clientId, out var room) && room != null)
            {
                if (room.owner == clientId)
                {
                    if (room.players.Contains(targetId))
                    {
                        var position = 0;
                        var buffer = buffers.Rent(maxPacketSize);
                        buffer.WriteByte(ref position, (byte)OpCodes.UpdateData);
                        buffer.WriteBytes(ref position, segment);
                        Program.transport.ServerSend(targetId, new ArraySegment<byte>(buffer, 0, position), channel);
                        buffers.Return(buffer);
                    }
                }
                else
                {
                    var position = 0;
                    var buffer = buffers.Rent(maxPacketSize);
                    buffer.WriteByte(ref position, (byte)OpCodes.UpdateData);
                    buffer.WriteBytes(ref position, segment);
                    buffer.WriteInt(ref position, clientId);
                    Program.transport.ServerSend(room.owner, new ArraySegment<byte>(buffer, 0, position), channel);
                    buffers.Return(buffer);
                }
            }
        }

        private void JoinRoom(int clientId, string serverId, bool isPunch, string address)
        {
            LeaveRoom(clientId);
            if (rooms.TryGetValue(serverId, out var room) && room.players.Count < room.count)
            {
                room.players.Add(clientId);
                clients.Add(clientId, room);
                var position = 0;
                var buffer = buffers.Rent(500);
                if (isPunch && Program.instance.connections.TryGetValue(clientId, out var connection))
                {
                    buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                    if (connection.Address.Equals(room.proxy.Address))
                    {
                        buffer.WriteString(ref position, room.address == address ? "127.0.0.1" : room.address);
                        Console.WriteLine("SendToClient:" + room.address + " " + address + " " + room.address == address ? "127.0.0.1" + ":" + room.proxy.Port : room.address + ":" + room.proxy.Port);
                    }
                    else
                    {
                        buffer.WriteString(ref position, room.proxy.Address.ToString());
                        Console.WriteLine("SendToClient:" + room.proxy.Address + ":" + room.proxy.Port);
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
                        Program.transport.ServerSend(room.owner, new ArraySegment<byte>(buffer, 0, position));
                        Console.WriteLine("SendToHost:" + connection.Address + ":" + connection.Port);
                    }

                    buffers.Return(buffer);
                    return;
                }

                buffer.WriteByte(ref position, (byte)OpCodes.JoinRoom);
                buffer.WriteInt(ref position, clientId);
                Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                Program.transport.ServerSend(room.owner, new ArraySegment<byte>(buffer, 0, position));
                buffers.Return(buffer);
            }
            else
            {
                var position = 0;
                var buffer = buffers.Rent(1);
                buffer.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
                Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                buffers.Return(buffer);
            }
        }

        private void CreateRoom(int clientId, int maxPlayers, string serverName, bool isPublic, string serverData, bool isDirect,
            string clientIp, bool isPunch, int clientPort)
        {
            LeaveRoom(clientId);
            Program.instance.connections.TryGetValue(clientId, out var proxy);
            var room = new Room
            {
                owner = clientId,
                count = maxPlayers,
                name = serverName,
                active = isPublic,
                data = serverData,
                players = new List<int>(),
                id = ServerId(),
                proxy = proxy,
                address = clientIp,
                punching = proxy != null && isDirect,
                port = clientPort,
                isPunch = isPunch
            };

            Console.WriteLine($"客户端{clientId}创建房间。" + room.id + " " + proxy + " " + clientIp);
            rooms.Add(room.id, room);
            clients.Add(clientId, room);
            var position = 0;
            var buffer = buffers.Rent(5);
            buffer.WriteByte(ref position, (byte)OpCodes.CreateRoom);
            buffer.WriteString(ref position, room.id);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            buffers.Return(buffer);
        }

        private void LeaveRoom(int clientId, int hostId = -1)
        {
            var copies = rooms.Values.ToList();
            foreach (var room in copies)
            {
                if (room.owner == clientId)
                {
                    var position = 0;
                    var buffer = buffers.Rent(1);
                    buffer.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
                    foreach (var client in room.players)
                    {
                        Program.transport.ServerSend(client, new ArraySegment<byte>(buffer, 0, position));
                        clients.Remove(client);
                    }

                    buffers.Return(buffer);
                    room.players.Clear();
                    rooms.Remove(room.id);
                    clients.Remove(clientId); //TODO:移除
                    return;
                }

                if (hostId != -1 && room.owner != hostId)
                {
                    continue;
                }

                if (room.players.RemoveAll(client => client == clientId) > 0)
                {
                    var position = 0;
                    var buffer = buffers.Rent(5);
                    buffer.WriteByte(ref position, (byte)OpCodes.Disconnect);
                    buffer.WriteInt(ref position, clientId);
                    Program.transport.ServerSend(room.owner, new ArraySegment<byte>(buffer, 0, position));
                    buffers.Return(buffer);
                    clients.Remove(clientId);
                }
            }
        }

        private string ServerId()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var random = new Random();
            string id;
            do
            {
                id = new string(Enumerable.Repeat(chars, 5).Select(s => s[random.Next(s.Length)]).ToArray());
            } while (rooms.ContainsKey(id));

            return id;
        }
    }
}