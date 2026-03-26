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
        Parking
    }
}
