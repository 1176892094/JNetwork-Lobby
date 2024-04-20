using System;
using JFramework.Net;
using UnityEngine;

[RequireComponent(typeof(NetworkRelayTransport))]
public class NetworkNATPuncher : MonoBehaviour
{
    public bool isDebug;
    public Transport transport;
    private NetworkRelayTransport relay;
    
    public bool isPunch => transport is NetworkTransport;

    private void Awake()
    {
        relay = GetComponent<NetworkRelayTransport>();

        if (transport == null)
        {
            Debug.Log("直连传输是空的！");
            return;
        }

        if (transport is NetworkRelayTransport)
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
            if (isDebug)
            {
                Debug.Log("NAT客户端连接到服务器。");
            }

            relay.NATServerConnected(clientId);
        }

        void OnServerReceive(int clientId, ArraySegment<byte> data, Channel channel)
        {
            relay.NATServerReceive(clientId, data, channel);
        }

        void OnServerDisconnected(int clientId)
        {
            relay.NATServerDisconnected(clientId);
        }

        void OnClientConnected()
        {
            if (isDebug)
            {
                Debug.Log("NAT客户端连接成功。");
            }

            relay.NATClientConnected();
        }

        void OnClientDisconnected()
        {
            relay.NATClientDisconnected();
        }

        void OnClientReceive(ArraySegment<byte> data, Channel channel)
        {
            relay.NATClientReceive(data, channel);
        }
    }

    public void StartServer(int port)
    {
        if (port > 0)
        {
            SetTransportPort(port);
        }

        if (isDebug)
        {
            Debug.Log("创建NAT服务器。");
        }

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
            SetTransportPort(port);
        }

        transport.address = ip;
        transport.ClientConnect();
    }

    public void SetTransportPort(int port)
    {
        if (transport is NetworkTransport udp)
        {
            udp.port = (ushort)port;
            return;
        }

        throw new Exception("NAT模块目前只支持Udp！");
    }

    public int GetTransportPort()
    {
        if (transport is NetworkTransport udp)
        {
            return udp.port;
        }

        throw new Exception("NAT模块目前只支持Udp！");
    }

    public void ServerDisconnect(int clientId)
    {
        if (isDebug)
        {
            Debug.Log("断开的NAT客户端。");
        }

        transport.ServerDisconnect(clientId);
    }

    public void ClientDisconnect()
    {
        if (isDebug)
        {
            Debug.Log("NAT客户端断开连接。");
        }

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