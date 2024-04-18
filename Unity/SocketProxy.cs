using System;
using System.Net;
using System.Net.Sockets;

namespace JFramework.Net
{
    public class SocketProxy
    {
        /// <summary>
        /// 交互时间
        /// </summary>
        public DateTime interactTime;

        /// <summary>
        /// 接收事件
        /// </summary>
        public event Action<IPEndPoint, byte[]> OnReceive;

        /// <summary>
        /// 客户端初始化接收
        /// </summary>
        private bool clientInitReceive;

        /// <summary>
        /// Udp客户端
        /// </summary>
        private readonly UdpClient udpClient;

        /// <summary>
        /// 远程端口
        /// </summary>
        private readonly IPEndPoint remoteEndPoint;

        /// <summary>
        /// 接收端口
        /// </summary>
        private IPEndPoint receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);

        /// <summary>
        /// 这是一个构造函数，初始化一个指向特定远程端点的UdpClient。在UdpClient上启动接收并设置最后交互时间。
        /// </summary>
        /// <param name="port"></param>
        /// <param name="endPoint"></param>
        public SocketProxy(int port, IPEndPoint endPoint)
        {
            udpClient = new UdpClient();
            udpClient.Connect(new IPEndPoint(IPAddress.Loopback, port));
            udpClient.BeginReceive(ReceiveData, udpClient);
            interactTime = DateTime.Now;
            remoteEndPoint = new IPEndPoint(endPoint.Address, endPoint.Port);
        }

        /// <summary>
        /// 这也是一个构造函数，它初始化一个监听特定端口的UdpClient。
        /// </summary>
        /// <param name="port"></param>
        public SocketProxy(int port)
        {
            udpClient = new UdpClient(port);
            udpClient.BeginReceive(ReceiveData, udpClient);
            interactTime = DateTime.Now;
        }

        /// <summary>
        /// 这个方法将一组数据发送给在构造函数中定义好的远程端点。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="length"></param>
        public void RemoteRelayData(byte[] data, int length)
        {
            udpClient.Send(data, length);
            interactTime = DateTime.Now;
        }

        /// <summary>
        /// 这个方法只有在客户端完成初始接收后才发送数据。它将数据发送到最近收到的数据包的来源。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="length"></param>
        public void ClientRelayData(byte[] data, int length)
        {
            if (clientInitReceive)
            {
                udpClient.Send(data, length, receiveEndPoint);
                interactTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 这是一个私有方法，用于处理接收到的数据。它读取数据，重新开始接收，并触发dataReceived事件。
        /// </summary>
        /// <param name="result"></param>
        private void ReceiveData(IAsyncResult result)
        {
            var data = udpClient.EndReceive(result, ref receiveEndPoint);
            udpClient.BeginReceive(ReceiveData, udpClient);
            clientInitReceive = true;
            interactTime = DateTime.Now;
            OnReceive?.Invoke(remoteEndPoint, data);
        }

        /// <summary>
        /// 清理函数，它关闭UdpClient。
        /// </summary>
        public void Dispose()
        {
            udpClient.Dispose();
        }
    }
}