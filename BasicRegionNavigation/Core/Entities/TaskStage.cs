using System.Collections.Generic;
using System.Text.Json.Serialization;
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
        /// 动态目标类型 (可选)，若配置则 TargetNodeId 作为动态寻址的接收变量
        /// </summary>
        [property: JsonConverter(typeof(JsonStringEnumConverter))]
        [ObservableProperty] private BasicRegionNavigation.Common.NodeType? _dynamicTargetType;

        /// <summary>
        /// 目标组名 (可选)，配合 DynamicTargetType 进一步筛选路由组
        /// </summary>
        [ObservableProperty] private List<string> _candidateZones = new List<string>();
    }
}
