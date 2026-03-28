using System.Collections.Generic;
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

        /// <summary>
        /// 关联的 PLC 动作码 (如 Plc_Load, Plc_Unload, None)
        /// </summary>
        [ObservableProperty] private string _actionCode;

        /// <summary>
        /// 动态目标类型 (可选)，值为 NodeType 枚举名称字符串 (如 "Wash", "Unload")，若配置则 TargetNodeId 作为动态寻址的接收变量
        /// </summary>
        public string DynamicTargetType { get; set; }

        /// <summary>
        /// 候选节点 ID 列表：由任务模板直接决定哪些具体的点参与动态分配
        /// </summary>
        public List<int> CandidateNodeIds { get; set; } = new List<int>();
    }
}
