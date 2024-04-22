using System;

namespace JFramework.Net
{
    [Serializable]
    internal class Setting
    {
        /// <summary>
        /// 程序集
        /// </summary>
        public string Assembly = "Transport.dll";
        
        /// <summary>
        /// 使用传输
        /// </summary>
        public string Transport = "JFramework.Net.NetworkTransport";
        
        /// <summary>
        /// 服务器密钥
        /// </summary>
        public string ServerKey = "Secret Key";
        
        /// <summary>
        /// 主线程循环时间
        /// </summary>
        public int UpdateTime = 10;
        
        /// <summary>
        /// 心跳
        /// </summary>
        public int HeartBeat = 100;
        
        /// <summary>
        /// 是否启用Rest服务
        /// </summary>
        public bool UseEndPoint = true;
        
        /// <summary>
        /// Rest服务器端口
        /// </summary>
        public ushort EndPointPort = 8080;
        
        /// <summary>
        /// 是否请求服务器列表
        /// </summary>
        public bool EndPointServerList = true;
        
        /// <summary>
        /// 使用内网穿透
        /// </summary>
        public bool UseNATPuncher = true;
        
        /// <summary>
        /// 内网穿透端口
        /// </summary>
        public ushort NATPunchPort = 7776;
    }
}