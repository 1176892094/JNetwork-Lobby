using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;

namespace JFramework.Net
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Room
    {
        /// <summary>
        /// 房间Id
        /// </summary>
        public string id;
        
        /// <summary>
        /// 房间名称
        /// </summary>
        public string name;
        
        /// <summary>
        /// 额外房间数据
        /// </summary>
        public string data;
        
        /// <summary>
        /// 房间拥有者
        /// </summary>
        public int ownerId;
        
        /// <summary>
        /// 房间最大人数
        /// </summary>
        public int maxCount;
        
        /// <summary>
        /// 是否显示
        /// </summary>
        public bool isPublic;
        
        /// <summary>
        /// 客户端数量
        /// </summary>
        public List<int> clients;
        
        /// <summary>
        /// 占用端口
        /// </summary>
        [JsonIgnore] public int port;
        
        /// <summary>
        /// 占用地址
        /// </summary>
        [JsonIgnore] public string address;
        
        /// <summary>
        /// 使用内网穿透
        /// </summary>
        [JsonIgnore] public bool isPunch;
        
        /// <summary>
        /// 内网穿透进行中
        /// </summary>
        [JsonIgnore] public bool punching;
        
        /// <summary>
        /// 客户端端口
        /// </summary>
        [JsonIgnore] public IPEndPoint owner;
    }

    /// <summary>
    /// 传输通道
    /// </summary>
    public enum Channel : byte
    {
        /// <summary>
        /// 可靠
        /// </summary>
        Reliable = 1,
        
        /// <summary>
        /// 不可靠
        /// </summary>
        Unreliable = 2
    }
}