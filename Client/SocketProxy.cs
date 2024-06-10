using System;
using System.Net;
using System.Net.Sockets;

namespace JFramework.Net
{
    public class SocketProxy
    {
        /// <summary>
        /// 代理是否活跃
        /// </summary>
        private bool isActive;

        /// <summary>
        /// 交互时间
        /// </summary>
        private DateTime dateTime;

        /// <summary>
        /// Udp客户端
        /// </summary>
        private readonly UdpClient punchClient;

        /// <summary>
        /// 远程端口
        /// </summary>
        private readonly IPEndPoint remoteEndPoint;

        /// <summary>
        /// 接收端口
        /// </summary>
        private IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);

        /// <summary>
        /// 距离上次交互的时间
        /// </summary>
        public double interval => DateTime.Now.Subtract(dateTime).TotalSeconds;

        /// <summary>
        /// 这是一个构造函数，初始化一个指向特定远程端点的UdpClient。在UdpClient上启动接收并设置最后交互时间。
        /// </summary>
        /// <param name="port"></param>
        /// <param name="OnReceive"></param>
        /// <param name="endPoint"></param>
        public SocketProxy(int port, Action<IPEndPoint, byte[]> OnReceive, IPEndPoint endPoint)
        {
            punchClient = new UdpClient();
            punchClient.Connect(new IPEndPoint(IPAddress.Loopback, port));
            punchClient.BeginReceive(ServerReceive, punchClient);
            remoteEndPoint = new IPEndPoint(endPoint.Address, endPoint.Port);
            dateTime = DateTime.Now;

            void ServerReceive(IAsyncResult result)
            {
                var data = punchClient.EndReceive(result, ref clientEndPoint);
                punchClient.BeginReceive(ServerReceive, punchClient);
                isActive = true;
                dateTime = DateTime.Now;
                OnReceive?.Invoke(remoteEndPoint, data);
            }
        }

        /// <summary>
        /// 这也是一个构造函数，它初始化一个监听特定端口的UdpClient。
        /// </summary>
        /// <param name="port"></param>
        /// <param name="OnReceive"></param>
        public SocketProxy(int port, Action<byte[]> OnReceive)
        {
            punchClient = new UdpClient(port);
            punchClient.BeginReceive(ClientReceive, punchClient);
            dateTime = DateTime.Now;

            void ClientReceive(IAsyncResult result)
            {
                var data = punchClient.EndReceive(result, ref clientEndPoint);
                punchClient.BeginReceive(ClientReceive, punchClient);
                isActive = true;
                dateTime = DateTime.Now;
                OnReceive?.Invoke(data);
            }
        }

        /// <summary>
        /// 这个方法将一组数据发送给在构造函数中定义好的远程端点。
        /// </summary>
        /// <param name="data"></param>
        public void SendToClient(byte[] data)
        {
            punchClient.Send(data, data.Length);
            dateTime = DateTime.Now;
        }

        /// <summary>
        /// 这个方法只有在客户端完成初始接收后才发送数据。它将数据发送到最近收到的数据包的来源。
        /// </summary>
        /// <param name="data"></param>
        public void SendToServer(byte[] data)
        {
            if (isActive)
            {
                punchClient.Send(data, data.Length, clientEndPoint);
                dateTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 清理函数，它关闭UdpClient。
        /// </summary>
        public void Dispose()
        {
            punchClient.Dispose();
            clientEndPoint = null;
        }
    }
}