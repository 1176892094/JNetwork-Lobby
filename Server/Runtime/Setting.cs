using System;

namespace JFramework.Net
{
    [Serializable]
    internal class Setting
    {
        public string Assembly = "Transport.dll";
        public string TransportClass = "JFramework.Net.NetworkTransport";
        public string AuthenticationKey = "Secret Auth Key";
        public int UpdateLoopTime = 10;
        public int UpdateHeartbeatInterval = 100;
        public bool UseEndPoint = true;
        public ushort EndpointPort = 8080;
        public bool EndpointServerList = true;
        public bool EnableNATPunchServer = true;
        public ushort NATPunchPort = 7776;
    }
}