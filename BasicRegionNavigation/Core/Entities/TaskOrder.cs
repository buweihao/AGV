using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BasicRegionNavigation.Core.Entities
{
    public partial class TaskOrder : ObservableObject
    {
        public string OrderId { get; set; } = "CMD-" + Guid.NewGuid().ToString().Substring(0, 5).ToUpper();
        
        [ObservableProperty]
        private int _targetNodeId;

        [ObservableProperty]
        private bool _isCompleted;

        [ObservableProperty]
        private string _status = "等待调度";

        [ObservableProperty]
        private string _assignedRobotId = "-";
    }
}
