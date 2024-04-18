using System;
using UnityEngine;

namespace JFramework.Net
{
    [RequireComponent(typeof(NetworkRelay))]
    public class NetworkPunch : MonoBehaviour
    {
        public bool isDebug;
        public Transport transport;
        private NetworkRelay relay;

        private void Awake()
        {
            relay = GetComponent<NetworkRelay>();

            if (transport == null)
            {
                Debug.Log("直连传输是空的！");
                return;
            }

            if (transport is NetworkRelay)
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
                    Debug.Log("直连客户端连接到服务器。");
                }

                relay.DirectAddClient(clientId);
            }

            void OnServerReceive(int clientId, ArraySegment<byte> data, Channel channel)
            {
                relay.DirectReceiveData(data, channel, clientId);
            }

            void OnServerDisconnected(int clientId)
            {
                relay.DirectRemoveClient(clientId);
            }

            void OnClientConnected()
            {
                if (isDebug)
                {
                    Debug.Log("直连客户端连接成功。");
                }

                relay.DirectClientConnected();
            }

            void OnClientDisconnected()
            {
                relay.DirectDisconnected();
            }

            void OnClientReceive(ArraySegment<byte> data, Channel channel)
            {
                relay.DirectReceiveData(data, channel);
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
                Debug.Log("创建直连服务器。");
            }

            transport.StartServer();
        }

        public void StopServer()
        {
            transport.StopServer();
        }

        public void JoinServer(string ip, int port)
        {
            if (IsPunch())
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

            throw new Exception("直连模块目前只支持Udp！");
        }

        public int GetTransportPort()
        {
            if (transport is NetworkTransport udp)
            {
                return udp.port;
            }

            throw new Exception("直连模块目前只支持Udp！");
        }

        public bool IsPunch()
        {
            return transport is NetworkTransport;
        }

        public void KickClient(int clientId)
        {
            if (isDebug)
            {
                Debug.Log("踢出的直连客户端。");
            }

            transport.ServerDisconnect(clientId);
        }

        public void ClientDisconnect()
        {
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
}