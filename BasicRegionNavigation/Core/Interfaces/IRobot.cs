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
        
        Task GoToNodeAsync(LogicNode targetNode);

        event Action<double, double> OnPositionChanged;
        event Action<int> OnNodeChanged;
        event Action<RobotState> OnRobotStateChanged;
        event Action<IRobot> OnBatteryLow;
    }
}
