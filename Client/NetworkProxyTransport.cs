using System;
using JFramework.Net;
using UnityEngine;

[RequireComponent(typeof(NetworkTransport))]
public class NetworkProxyTransport : MonoBehaviour
{
    /// <summary>
    /// 使用的传输
    /// </summary>
    public Transport transport;

    /// <summary>
    /// 初始化使用大厅的委托
    /// </summary>
    private void Awake()
    {
        transport = GetComponent<NetworkTransport>();
        var lobby = GetComponentInParent<NetworkLobbyTransport>();
        transport.OnServerConnected = lobby.NATServerConnected;
        transport.OnServerReceive = lobby.NATServerReceive;
        transport.OnServerDisconnected = lobby.NATServerDisconnected;
        transport.OnClientConnected = lobby.NATClientConnected;
        transport.OnClientReceive = lobby.NATClientReceive;
        transport.OnClientDisconnected = lobby.NATClientDisconnected;
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
        transport.StopServer();
    }

    /// <summary>
    /// 有客户端从主机断开
    /// </summary>
    /// <param name="clientId"></param>
    public void ServerDisconnect(int clientId)
    {
        transport.ServerDisconnect(clientId);
    }

    /// <summary>
    /// 客户端断开
    /// </summary>
    public void ClientDisconnect()
    {
        transport.ClientDisconnect();
    }

    /// <summary>
    /// 主机向客户端发送消息
    /// </summary>
    /// <param name="clientId"></param>
    /// <param name="data"></param>
    /// <param name="channel"></param>
    public void ServerSend(int clientId, ArraySegment<byte> data, Channel channel)
    {
        transport.ServerSend(clientId, data, channel);
    }

    /// <summary>
    /// 客户端发送消息到主机
    /// </summary>
    /// <param name="data"></param>
    /// <param name="channel"></param>
    public void ClientSend(ArraySegment<byte> data, Channel channel)
    {
        transport.ClientSend(data, channel);
    }
}