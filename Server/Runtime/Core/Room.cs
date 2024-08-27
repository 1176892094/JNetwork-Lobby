using System;
using System.Collections.Generic;

namespace JFramework.Net
{
    [Serializable]
    public class Room
    {
        /// <summary>
        /// 房间Id
        /// </summary>
        public string roomId;

        /// <summary>
        /// 房间名称
        /// </summary>
        public string roomName;

        /// <summary>
        /// 额外房间数据
        /// </summary>
        public string roomData;

        /// <summary>
        /// 房间最大人数
        /// </summary>
        public int maxCount;
        
        /// <summary>
        /// 房间拥有者
        /// </summary>
        public int clientId;

        /// <summary>
        /// 是否显示
        /// </summary>
        public bool isPublic;

        /// <summary>
        /// 客户端数量
        /// </summary>
        public HashSet<int> clients;
    }
}