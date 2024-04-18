using System;
using System.Collections.Generic;
using System.Text;

namespace JFramework.Net
{
    internal class Config
    {
        public string TransportDLL = "Transport.dll";
        public string TransportClass = "JFramework.Net.NetworkTransport";
        public string AuthenticationKey = "Secret Auth Key";
        public int UpdateLoopTime = 10;
        public int UpdateHeartbeatInterval = 100;
        public bool UseEndpoint = true;
        public ushort EndpointPort = 8080;
        public bool EndpointServerList = true;
        public bool EnableNATPunchtroughServer = true;
        public ushort NATPunchtroughPort = 7776;
    }
}