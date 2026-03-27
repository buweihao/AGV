namespace BasicRegionNavigation.Common
{
    public enum NodeType
    {
        /// <summary>
        /// 普通节点
        /// </summary>
        Normal,

        /// <summary>
        /// 充电桩节点
        /// </summary>
        Charging,

        /// <summary>
        /// 安全等待区/避让区
        /// </summary>
        Safety,

        /// <summary>
        /// 工作站/取货点
        /// </summary>
        Station,

        /// <summary>
        /// 停放点/等待位
        /// </summary>
        Parking,

        // --- 以下为需要新增的类型 ---

        /// <summary>
        /// 清洗节点
        /// </summary>
        Wash,

        /// <summary>
        /// 卸料/下料节点
        /// </summary>
        Unload,

        /// <summary>
        /// 装料/上料节点
        /// </summary>
        Load,

        /// <summary>
        /// 缓冲节点
        /// </summary>
        Buffer
    }
}