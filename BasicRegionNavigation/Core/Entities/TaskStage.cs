using CommunityToolkit.Mvvm.ComponentModel;

namespace BasicRegionNavigation.Core.Entities
{
    public partial class TaskStage : ObservableObject
    {
        /// <summary>
        /// 本阶段的目标节点 ID
        /// </summary>
        [ObservableProperty] private int _targetNodeId;

        /// <summary>
        /// 到达后在此节点的停留/动作时间 (毫秒)
        /// </summary>
        [ObservableProperty] private int _waitTimeMs;

        /// <summary>
        /// 阶段描述，例如“前往取货点”、“等待装料”
        /// </summary>
        [ObservableProperty] private string _stageName;
    }
}
