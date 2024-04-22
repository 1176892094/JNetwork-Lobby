using System;
using JFramework;
using JFramework.Net;

public class NetworkProxyTransport : Component<NetworkLobbyTransport>
{
    /// <summary>
    /// 使用的传输
    /// </summary>
    private Transport transport;

    /// <summary>
    /// 使用传输的端口
    /// </summary>
    public int port => transport.port;

    /// <summary>
    /// 初始化使用大厅的委托
    /// </summary>
    private void Awake()
    {
        transport = owner.puncher;
        transport.OnServerConnected -= owner.NATServerConnected;
        transport.OnServerReceive -= owner.NATServerReceive;
        transport.OnServerDisconnected -= owner.NATServerDisconnected;
        transport.OnClientConnected -= owner.NATClientConnected;
        transport.OnClientReceive -= owner.NATClientReceive;
        transport.OnClientDisconnected -= owner.NATClientDisconnected;
        transport.OnServerConnected += owner.NATServerConnected;
        transport.OnServerReceive += owner.NATServerReceive;
        transport.OnServerDisconnected += owner.NATServerDisconnected;
        transport.OnClientConnected += owner.NATClientConnected;
        transport.OnClientReceive += owner.NATClientReceive;
        transport.OnClientDisconnected += owner.NATClientDisconnected;
    }

    /// <summary>
    /// 开启主机
    /// </summary>
    /// <param name="port"></param>
    public void StartServer(int port)
    {
        if (port > 0)
        {
            transport.port = (ushort)port;
        }

        transport.StartServer();
    }

    /// <summary>
    /// 加入到主机
    /// </summary>
    /// <param name="ip"></param>
    /// <param name="port"></param>
    public void JoinServer(string ip, int port)
    {
        if (transport is NetworkTransport)
        {
            transport.address = ip;
            transport.port = (ushort)port;
        }

        transport.ClientConnect();
    }

    /// <summary>
    /// 停止主机
    /// </summary>
    public void StopServer()
    {
        if (transport != null)
        {
            transport.StopServer();
        }
    }

    /// <summary>
    /// 有客户端从主机断开
    /// </summary>
    /// <param name="clientId"></param>
    public void ServerDisconnect(int clientId)
    {
        if (transport != null)
        {
            transport.ServerDisconnect(clientId);
        }
    }

    /// <summary>
    /// 客户端断开
    /// </summary>
    public void ClientDisconnect()
    {
        if (transport != null)
        {
            transport.ClientDisconnect();
        }
    }

    /// <summary>
    /// 主机向客户端发送消息
    /// </summary>
    /// <param name="clientId"></param>
    /// <param name="segment"></param>
    /// <param name="channel"></param>
    public void ServerSend(int clientId, ArraySegment<byte> segment, Channel channel)
    {
        if (transport != null)
        {
            transport.ServerSend(clientId, segment, channel);
        }
    }

    /// <summary>
    /// 客户端发送消息到主机
    /// </summary>
    /// <param name="segment"></param>
    /// <param name="channel"></param>
    public void ClientSend(ArraySegment<byte> segment, Channel channel)
    {
        if (transport != null)
        {
            transport.ClientSend(segment, channel);
        }
    }

    /// <summary>
    /// 客户端Update之前
    /// </summary>
    public void ClientEarlyUpdate()
    {
        if (transport != null)
        {
            transport.ClientEarlyUpdate();
        }
    }

    /// <summary>
    /// 客户端Update之后
    /// </summary>
    public void ClientAfterUpdate()
    {
        if (transport != null)
        {
            transport.ClientAfterUpdate();
        }
    }

    /// <summary>
    /// 服务器Update之前
    /// </summary>
    public void ServerEarlyUpdate()
    {
        if (transport != null)
        {
            transport.ServerEarlyUpdate();
        }
    }

    /// <summary>
    /// 服务器Update之后
    /// </summary>
    public void ServerAfterUpdate()
    {
        if (transport != null)
        {
            transport.ServerAfterUpdate();
        }
    }
}