using System;

namespace JFramework.Net
{
    [Serializable]
    internal class Setting
    {
        public string Assembly = "Transport.dll";
        public string Transport = "JFramework.Net.NetworkTransport";
        public string ServerKey = "Secret Key";
        public int UpdateTime = 10;
        public int HeartBeat = 100;
        public bool UseEndPoint = true;
        public ushort EndPointPort = 8080;
        public bool EndPointServerList = true;
        public bool UseNATPuncher = true;
        public ushort NATPunchPort = 7776;
    }
}