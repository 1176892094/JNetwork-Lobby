using System;
using System.Collections.Generic;
using System.Linq;

namespace JFramework.Net
{
    public class Process
    {
        public readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        private readonly Dictionary<int, Room> clients = new Dictionary<int, Room>();
        private readonly HashSet<int> connections = new HashSet<int>();
        private readonly string serverKey;
        private readonly Transport transport;
        private readonly Random random = new Random();

        public Process(Transport transport, string serverKey)
        {
            this.transport = transport;
            this.serverKey = serverKey;
        }

        public void ServerConnected(int clientId)
        {
            connections.Add(clientId);
            using var writer = NetworkWriter.Pop();
            writer.WriteByte((byte)OpCodes.Connect);
            transport.SendToClient(clientId, writer);
        }

        public void ServerDisconnected(int clientId, int owner = -1)
        {
            var copies = rooms.Values.ToList();
            foreach (var room in copies)
            {
                if (room.ownerId == clientId)
                {
                    using var writer = NetworkWriter.Pop();
                    writer.WriteByte((byte)OpCodes.LeaveRoom);
                    foreach (var client in room.clients)
                    {
                        transport.SendToClient(client, writer);
                        clients.Remove(client);
                    }

                    room.clients.Clear();
                    rooms.Remove(room.id);
                    clients.Remove(clientId);
                    return;
                }

                if (owner != -1 && room.ownerId != owner)
                {
                    continue;
                }

                if (room.clients.RemoveAll(client => client == clientId) > 0)
                {
                    using var writer = NetworkWriter.Pop();
                    writer.WriteByte((byte)OpCodes.Disconnect);
                    writer.WriteInt(clientId);
                    transport.SendToClient(room.ownerId, writer);
                    clients.Remove(clientId);
                }
            }
        }

        public void ServerReceive(int clientId, ArraySegment<byte> segment, byte channel)
        {
            try
            {
                using var reader = NetworkReader.Pop(segment);
                var opcode = (OpCodes)reader.ReadByte();
                if (opcode == OpCodes.Connected)
                {
                    if (connections.Contains(clientId))
                    {
                        var message = reader.ReadString();
                        if (message == serverKey)
                        {
                            using var writer = NetworkWriter.Pop();
                            writer.WriteByte((byte)OpCodes.Connected);
                            transport.SendToClient(clientId, writer);
                            connections.Remove(clientId);
                        }
                    }
                }
                else if (opcode == OpCodes.CreateRoom)
                {
                    ServerDisconnected(clientId);
                    string id;
                    do
                    {
                        id = new string(Enumerable.Repeat("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 5).Select(s => s[random.Next(s.Length)]).ToArray());
                    } while (rooms.ContainsKey(id));

                    var room = new Room
                    {
                        id = id,
                        roomName = reader.ReadString(),
                        roomData = reader.ReadString(),
                        ownerId = clientId,
                        maxCount = reader.ReadInt(),
                        isPublic = reader.ReadBool(),
                        clients = new List<int>(),
                    };
                    rooms.Add(room.id, room);
                    clients.Add(clientId, room);
                    Debug.Log($"客户端 {clientId} 创建游戏房间。");
                    using var writer = NetworkWriter.Pop();
                    writer.WriteByte((byte)OpCodes.CreateRoom);
                    writer.WriteString(room.id);
                    transport.SendToClient(clientId, writer);
                }
                else if (opcode == OpCodes.JoinRoom)
                {
                    var ownerId = reader.ReadString();
                    ServerDisconnected(clientId);
                    if (rooms.TryGetValue(ownerId, out var room) && room.clients.Count + 1 < room.maxCount)
                    {
                        room.clients.Add(clientId);
                        clients.Add(clientId, room);
                        using var writer = NetworkWriter.Pop();
                        writer.WriteByte((byte)OpCodes.JoinRoom);
                        writer.WriteInt(clientId);
                        transport.SendToClient(clientId, writer);
                        transport.SendToClient(room.ownerId, writer);
                    }
                    else
                    {
                        using var writer = NetworkWriter.Pop();
                        writer.WriteByte((byte)OpCodes.LeaveRoom);
                        transport.SendToClient(clientId, writer);
                    }
                }
                else if (opcode == OpCodes.UpdateRoom)
                {
                    if (clients.TryGetValue(clientId, out var room))
                    {
                        room.roomName = reader.ReadString();
                        room.roomData = reader.ReadString();
                        room.isPublic = reader.ReadBool();
                        room.maxCount = reader.ReadInt();
                    }
                }
                else if (opcode == OpCodes.LeaveRoom)
                {
                    ServerDisconnected(clientId);
                }
                else if (opcode == OpCodes.UpdateData)
                {
                    var newData = reader.ReadArraySegment();
                    var targetId = reader.ReadInt();
                    if (clients.TryGetValue(clientId, out var room) && room != null)
                    {
                        if (room.ownerId == clientId)
                        {
                            if (room.clients.Contains(targetId))
                            {
                                using var writer = NetworkWriter.Pop();
                                writer.WriteByte((byte)OpCodes.UpdateData);
                                writer.WriteArraySegment(newData);
                                transport.SendToClient(targetId, writer, channel);
                            }
                        }
                        else
                        {
                            using var writer = NetworkWriter.Pop();
                            writer.WriteByte((byte)OpCodes.UpdateData);
                            writer.WriteArraySegment(newData);
                            writer.WriteInt(clientId);
                            transport.SendToClient(room.ownerId, writer, channel);
                        }
                    }
                }
                else if (opcode == OpCodes.Disconnect)
                {
                    ServerDisconnected(reader.ReadInt(), clientId);
                }
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString(), ConsoleColor.Red);
                transport.StopClient(clientId);
            }
        }
    }

    /// <summary>
    /// 操作符
    /// </summary>
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
    }
}