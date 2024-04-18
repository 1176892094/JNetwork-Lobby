namespace JFramework.Net
{
    public class Setting
    {
        /// <summary>
        /// 需求设置
        /// </summary>
        public int UpdateLoopTime = 10;
        public int HeartBeatInterval = 100;
        public ushort TransportPort = 9987;
        public string AuthenticationKey = "Secret Auth Key";
        public string TransportClass = "JFramework.Net.NetworkTransport";

        /// <summary>
        /// 使用负载均衡器，则不会使用此字段
        /// </summary>
        public int RandomIdLength = 5;

        /// <summary>
        /// 中继服务器设置
        /// </summary>
        public bool UseEndpoint = true;
        public bool EndpointServerList = true;
        public ushort RelayPort = 8080;

        /// <summary>
        /// 内网穿透设置
        /// </summary>
        public bool EnablePunchServer = true;
        public ushort PuncherPort = 7776;

        /// <summary>
        /// 负载均衡器设置
        /// </summary>
        public bool UseLoadBalancer = false;
        public ushort LoadBalancerPort = 7070;
        public string LoadBalancerAuthKey = "AuthKey";
        public string LoadBalancerAddress = "127.0.0.1";
        public Regions LoadBalancerRegion = Regions.NorthAmerica;

        public static string Assembly => "Transport.dll";
    }
}