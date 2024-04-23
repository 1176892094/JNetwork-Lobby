using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace JFramework.Net
{
    public class RelayHelper
    {
        public readonly Dictionary<int, Room> clients = new Dictionary<int, Room>();
        public readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        private readonly int length;
        private readonly ArrayPool<byte> buffers;
        private readonly Random random = new Random();
        private readonly List<int> connections = new List<int>();
        private const string CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public RelayHelper(int length)
        {
            this.length = length;
            buffers = ArrayPool<byte>.Create(length, 50);
        }

        public void ServerConnected(int clientId)
        {
            connections.Add(clientId);
            var position = 0;
            var buffer = buffers.Rent(1);
            buffer.WriteByte(ref position, (byte)OpCodes.Connected);
            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
            buffers.Return(buffer);
        }

        public void ServerDisconnected(int clientId, int owner = -1)
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
                    clients.Remove(clientId);
                    return;
                }

                if (owner != -1 && room.owner != owner)
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

        public void ServerReceive(int clientId, ArraySegment<byte> segment, Channel channel)
        {
            try
            {
                var data = segment.Array;
                var position = segment.Offset;
                var opcode = (OpCodes)data.ReadByte(ref position);

                if (opcode == OpCodes.Authority)
                {
                    if (connections.Contains(clientId))
                    {
                        var secretKey = data.ReadString(ref position);
                        if (secretKey == Program.setting.ServerKey)
                        {
                            position = 0;
                            var buffer = buffers.Rent(1);
                            buffer.WriteByte(ref position, (byte)OpCodes.Authority);
                            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                            buffers.Return(buffer);
                            connections.Remove(clientId);
                        }
                    }
                }
                else if (opcode == OpCodes.CreateRoom)
                {
                    ServerDisconnected(clientId);
                    Program.instance.connections.TryGetValue(clientId, out var connection);
                    string id;
                    do
                    {
                        id = new string(Enumerable.Repeat(CHARS, 5).Select(s => s[random.Next(s.Length)]).ToArray());
                    } while (rooms.ContainsKey(id));

                    var room = new Room
                    {
                        id = id,
                        name = data.ReadString(ref position),
                        data = data.ReadString(ref position),
                        owner = clientId,
                        count = data.ReadInt(ref position),
                        active = data.ReadBool(ref position),
                        players = new List<int>(),
                        address = data.ReadString(ref position),
                        port = data.ReadInt(ref position),
                        isPunch = data.ReadBool(ref position),
                        punching = connection != null && data.ReadBool(ref position),
                        proxy = connection,
                    };
                    rooms.Add(room.id, room);
                    clients.Add(clientId, room);
                    Console.WriteLine($"客户端 {clientId} 创建房间。{room.id} {room.address}:{room.port} {(connection == null ? "Null" : connection)}");

                    position = 0;
                    var buffer = buffers.Rent(50);
                    buffer.WriteByte(ref position, (byte)OpCodes.CreateRoom);
                    buffer.WriteString(ref position, room.id);
                    Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                    buffers.Return(buffer);
                }
                else if (opcode == OpCodes.JoinRoom)
                {
                    var id = data.ReadString(ref position);
                    var isPunch = data.ReadBool(ref position);
                    ServerDisconnected(clientId);
                    if (rooms.TryGetValue(id, out var room) && room.players.Count < room.count)
                    {
                        room.players.Add(clientId);
                        clients.Add(clientId, room);
                        position = 0;
                        var buffer = buffers.Rent(500);
                        if (isPunch && Program.instance.connections.TryGetValue(clientId, out var connection) && room.punching)
                        {
                            buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                            buffer.WriteString(ref position, room.proxy.Address.ToString());
                            buffer.WriteInt(ref position, room.isPunch ? room.proxy.Port : room.port);
                            buffer.WriteBool(ref position, room.isPunch);
                            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                            Console.WriteLine($"客户端 {clientId} 加入房间。" + room.proxy.Address + ":" + room.proxy.Port);
                            if (room.isPunch) // 给主机发送连接者的地址
                            {
                                position = 0;
                                buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                                buffer.WriteString(ref position, connection.Address.ToString());
                                buffer.WriteInt(ref position, connection.Port);
                                buffer.WriteBool(ref position, true);
                                buffer.WriteInt(ref position, clientId);
                                Program.transport.ServerSend(room.owner, new ArraySegment<byte>(buffer, 0, position));
                            }
                        }
                        else
                        {
                            buffer.WriteByte(ref position, (byte)OpCodes.JoinRoom);
                            buffer.WriteInt(ref position, clientId);
                            Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                            Program.transport.ServerSend(room.owner, new ArraySegment<byte>(buffer, 0, position));
                        }

                        buffers.Return(buffer);
                    }
                    else
                    {
                        position = 0;
                        var buffer = buffers.Rent(1);
                        buffer.WriteByte(ref position, (byte)OpCodes.LeaveRoom);
                        Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                        buffers.Return(buffer);
                    }
                }
                else if (opcode == OpCodes.UpdateRoom)
                {
                    if (clients.TryGetValue(clientId, out var room))
                    {
                        room.name = data.ReadString(ref position) ?? "Room";
                        room.data = data.ReadString(ref position) ?? "1";
                        room.active = data.ReadBool(ref position);
                        room.count = data.ReadInt(ref position);
                    }
                }
                else if (opcode == OpCodes.LeaveRoom)
                {
                    ServerDisconnected(clientId);
                }
                else if (opcode == OpCodes.UpdateData)
                {
                    var newData = data.ReadBytes(ref position);
                    var targetId = data.ReadInt(ref position);
                    if (clients.TryGetValue(clientId, out var room) && room != null)
                    {
                        if (room.owner == clientId)
                        {
                            if (room.players.Contains(targetId))
                            {
                                position = 0;
                                var buffer = buffers.Rent(length);
                                buffer.WriteByte(ref position, (byte)OpCodes.UpdateData);
                                buffer.WriteBytes(ref position, newData);
                                Program.transport.ServerSend(targetId, new ArraySegment<byte>(buffer, 0, position), channel);
                                buffers.Return(buffer);
                            }
                        }
                        else
                        {
                            position = 0;
                            var buffer = buffers.Rent(length);
                            buffer.WriteByte(ref position, (byte)OpCodes.UpdateData);
                            buffer.WriteBytes(ref position, newData);
                            buffer.WriteInt(ref position, clientId);
                            Program.transport.ServerSend(room.owner, new ArraySegment<byte>(buffer, 0, position), channel);
                            buffers.Return(buffer);
                        }
                    }
                }
                else if (opcode == OpCodes.Disconnect)
                {
                    ServerDisconnected(data.ReadInt(ref position), clientId);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    /// <summary>
    /// 操作符
    /// </summary>
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