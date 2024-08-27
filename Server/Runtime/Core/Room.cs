using Newtonsoft.Json;
using System.Collections.Generic;

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
        public string roomName;

        /// <summary>
        /// 额外房间数据
        /// </summary>
        public string roomData;

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
    }
}