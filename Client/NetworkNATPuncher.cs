using System;
using JFramework.Net;
using UnityEngine;

public class NetworkNATPuncher : MonoBehaviour
{
    public Transport transport;
    public bool isPunch => transport is NetworkTransport;

    private void Awake()
    {
        var lobby = GetComponent<NetworkLobbyTransport>();

        if (transport == null)
        {
            Debug.Log("直连传输是空的！");
            return;
        }

        if (transport is NetworkLobbyTransport)
        {
            Debug.Log("直连传输不能是中继！");
            return;
        }

        transport.OnServerConnected = OnServerConnected;
        transport.OnServerReceive = OnServerReceive;
        transport.OnServerDisconnected = OnServerDisconnected;
        transport.OnClientConnected = OnClientConnected;
        transport.OnClientReceive = OnClientReceive;
        transport.OnClientDisconnected = OnClientDisconnected;

        void OnServerConnected(int clientId)
        {
            Debug.Log($"NAT客户端{clientId}连接到服务器。");
            lobby.NATServerConnected(clientId);
        }

        void OnServerReceive(int clientId, ArraySegment<byte> data, Channel channel)
        {
            lobby.NATServerReceive(clientId, data, channel);
        }

        void OnServerDisconnected(int clientId)
        {
            lobby.NATServerDisconnected(clientId);
        }

        void OnClientConnected()
        {
            Debug.Log("NAT客户端连接成功。");
            lobby.NATClientConnected();
        }

        void OnClientDisconnected()
        {
            lobby.NATClientDisconnected();
        }

        void OnClientReceive(ArraySegment<byte> data, Channel channel)
        {
            lobby.NATClientReceive(data, channel);
        }
    }

    public void StartServer(int port)
    {
        if (port > 0)
        {
            transport.port = (ushort)port;
        }

        Debug.Log("开启NAT服务器。");
        transport.StartServer();
    }

    public void StopServer()
    {
        transport.StopServer();
    }

    public void JoinServer(string ip, int port)
    {
        if (isPunch)
        {
            transport.port = (ushort)port;
            transport.address = ip;
        }

        transport.ClientConnect();
    }

    public void ServerDisconnect(int clientId)
    {
        Debug.Log($"NAT断开客户端{clientId}");
        transport.ServerDisconnect(clientId);
    }

    public void ClientDisconnect()
    {
        Debug.Log("NAT客户端断开连接。");
        transport.ClientDisconnect();
    }

    public void ServerSend(int clientId, ArraySegment<byte> data, Channel channel)
    {
        transport.ServerSend(clientId, data, channel);
    }

    public void ClientSend(ArraySegment<byte> data, Channel channel)
    {
        transport.ClientSend(data, channel);
    }
}