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
        [NotifyPropertyChangedFor(nameof(StatusDisplayText))]
        private TaskStatus _status = TaskStatus.Waiting;

        /// <summary>
        /// 阶段性描述 (用于测试场景，如“直行”、“南北贯穿”等描述性文字)
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusDisplayText))]
        private string _stageDescription = string.Empty;

        [ObservableProperty]
        private string _assignedRobotId = "-";

        /// <summary>
        /// 格式化后的状态显示 (供 UI 绑定)
        /// </summary>
        public string StatusDisplayText
        {
            get
            {
                string state = Status switch
                {
                    TaskStatus.Waiting => "等待调度",
                    TaskStatus.PreChecking => "预检避让中",
                    TaskStatus.Executing => "执行中",
                    TaskStatus.Completed => "已完成",
                    TaskStatus.Cancelled => "已取消",
                    TaskStatus.Fault => "异常/死锁",
                    _ => "未知"
                };

                return string.IsNullOrEmpty(StageDescription) ? state : $"{state} ({StageDescription})";
            }
        }
    }
}
