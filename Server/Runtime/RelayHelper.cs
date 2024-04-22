﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;

namespace JFramework.Net
{
    public class RelayHelper
    {
        private const string CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private readonly int messageSize;
        private readonly Random random = new Random();
        private readonly ArrayPool<byte> buffers;
        private readonly List<int> connections = new List<int>();
        public readonly Dictionary<int, Room> clients = new Dictionary<int, Room>();
        public readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();

        public RelayHelper(int messageSize)
        {
            this.messageSize = messageSize;
            buffers = ArrayPool<byte>.Create(messageSize, 50);
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

        public void ServerReceive(int clientId, ArraySegment<byte> segment, Channel channel)
        {
            try
            {
                var data = segment.Array;
                var position = segment.Offset;
                var key = data.ReadByte(ref position);
                var opcode = (OpCodes)key;
                if (key != byte.MaxValue && key != 7)
                {
                    Console.WriteLine(opcode);
                }

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
                    var count = data.ReadInt(ref position);
                    var name = data.ReadString(ref position);
                    var active = data.ReadBool(ref position);
                    var roomData = data.ReadString(ref position);
                    var punching = data.ReadBool(ref position);
                    var address = data.ReadString(ref position);
                    var isPunch = data.ReadBool(ref position);
                    var port = data.ReadInt(ref position);
                    ServerDisconnected(clientId);
                    if (Program.instance.connections.TryGetValue(clientId, out var proxy))
                    {
                        var id = new string(Enumerable.Repeat(CHARS, 5).Select(s => s[random.Next(s.Length)]).ToArray());
                        while (rooms.ContainsKey(id))
                        {
                            id = new string(Enumerable.Repeat(CHARS, 5).Select(s => s[random.Next(s.Length)]).ToArray());
                        }

                        var room = new Room
                        {
                            id = id,
                            name = name,
                            data = roomData,
                            owner = clientId,
                            count = count,
                            active = active,
                            players = new List<int>(),
                            port = port,
                            address = address,
                            isPunch = isPunch,
                            punching = proxy != null && punching,
                            proxy = proxy,
                        };

                        Console.WriteLine($"客户端 {clientId} 创建房间。" + room.id + " " + proxy + " " + address);
                        rooms.Add(room.id, room);
                        clients.Add(clientId, room);
                        position = 0;
                        var buffer = buffers.Rent(5);
                        buffer.WriteByte(ref position, (byte)OpCodes.CreateRoom);
                        buffer.WriteString(ref position, room.id);
                        Program.transport.ServerSend(clientId, new ArraySegment<byte>(buffer, 0, position));
                        buffers.Return(buffer);
                    }
                }
                else if (opcode == OpCodes.JoinRoom)
                {
                    var serverId = data.ReadString(ref position);
                    var isPunch = data.ReadBool(ref position);
                    var address = data.ReadString(ref position);
                    ServerDisconnected(clientId);
                    if (rooms.TryGetValue(serverId, out var room) && room.players.Count < room.count)
                    {
                        room.players.Add(clientId);
                        clients.Add(clientId, room);
                        position = 0;
                        var buffer = buffers.Rent(500);
                        if (isPunch && Program.instance.connections.TryGetValue(clientId, out var connection))
                        {
                            buffer.WriteByte(ref position, (byte)OpCodes.NATAddress);
                            if (connection.Address.Equals(room.proxy.Address))
                            {
                                buffer.WriteString(ref position, room.address == address ? "127.0.0.1" : room.address);
                                Console.WriteLine("SendToClient:" + room.address + " " + address + " " + room.address == address
                                    ? "127.0.0.1" + ":" + room.proxy.Port
                                    : room.address + ":" + room.proxy.Port);
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
                                var buffer = buffers.Rent(messageSize);
                                buffer.WriteByte(ref position, (byte)OpCodes.UpdateData);
                                buffer.WriteBytes(ref position, newData);
                                Program.transport.ServerSend(targetId, new ArraySegment<byte>(buffer, 0, position), channel);
                                buffers.Return(buffer);
                            }
                        }
                        else
                        {
                            position = 0;
                            var buffer = buffers.Rent(messageSize);
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
            catch
            {
                // ignored
            }
        }

        public void ServerDisconnected(int clientId, int hostId = -1)
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