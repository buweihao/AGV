using System;
using MyLog;

namespace BasicRegionNavigation.Services
{
    /// <summary>
    /// 全局日志服务接口，对接底层 Serilog 静态引擎与 MyLog 扩展。
    /// 请调用方严格遵守以下日志等级规范：
    /// </summary>
    public interface ILoggerService:IMyLogConfig
    {
        /// <summary>
        /// [Debug] 仅用于核心算法追踪、数据流细节显示。
        /// 示例场景：PathFinder 计算出了从节点 1 到 5 的寻路序列。
        /// </summary>
        void OnDebug(string message, Exception ex = null);

        /// <summary>
        /// [Information] 用于记录业务状态的变更、关键任务的开启与完成。
        /// 示例场景：小车 AGV-1 到达节点 7；任务单 1002 已转为“执行中”。
        /// </summary>
        void OnInfo(string message, Exception ex = null);

        /// <summary>
        /// [Warning] 用于记录非致命的异常状态，如交通管制、等待、重试。
        /// 示例场景：小车 AGV-2 正在路口 0 等待 AGV-1 释放 Zone 2。
        /// </summary>
        void OnWarn(string message, Exception ex = null);

        /// <summary>
        /// [Error] 用于系统崩溃、核心组件连接断开、或者检测到死锁等关键错误。
        /// 示例场景：无法建立与 PLC 的连接；发现循环等待导致系统死锁。
        /// </summary>
        void OnError(string message, Exception ex = null);
        void testDebug();
    }
}
