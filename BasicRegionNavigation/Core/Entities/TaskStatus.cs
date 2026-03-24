namespace BasicRegionNavigation.Core.Entities
{
    /// <summary>
    /// 任务状态枚举
    /// </summary>
    public enum TaskStatus
    {
        /// <summary>
        /// 等待调度 (待派发)
        /// </summary>
        Waiting,

        /// <summary>
        /// 预检中 (此时正在处理终点占用或避让逻辑)
        /// </summary>
        PreChecking,

        /// <summary>
        /// 执行中 (小车正在移动)
        /// </summary>
        Executing,

        /// <summary>
        /// 已到达/已完成
        /// </summary>
        Completed,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled,

        /// <summary>
        /// 异常/故障
        /// </summary>
        Fault
    }
}
