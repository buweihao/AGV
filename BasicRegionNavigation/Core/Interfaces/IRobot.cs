using System;
using System.Threading.Tasks;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Common;

namespace BasicRegionNavigation.Core.Interfaces
{
    public interface IRobot
    {
        string Id { get; }
        int CurrentNode { get; set; }
        double CurrentX { get; }
        double CurrentY { get; }
        RobotState State { get; }
        string CurrentStateText { get; }
        string CurrentTaskDesc { get; set; }
        double BatteryLevel { get; set; }
        
        // --- 统计指标 ---
        double DistancePerMinute { get; }
        double TasksPerMinute { get; }
        double BatteryPerMinute { get; }
        double MaxStopSec { get; }
        string MaxStopReasonText { get; }

        Task GoToNodeAsync(LogicNode targetNode);

        /// <summary>
        /// 记录一次非正常停止（如交通拥堵、等待解锁等）
        /// </summary>
        void RecordStop(string reason);

        event Action<double, double> OnPositionChanged;
        event Action<int> OnNodeChanged;
        event Action<RobotState> OnRobotStateChanged;
        event Action<IRobot> OnBatteryLow;
    }
}
