using BasicRegionNavigation.Common;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BasicRegionNavigation.Core.Entities
{
    public class LogicNode
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        
        // 专职避让路点
        public bool IsBufferNode { get; set; } = false;

        // 区域管制名称 (如 Zone_A)
        public string ZoneName { get; set; }

        // 节点类型 (枚举替代原本的 string)
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public NodeType NodeType { get; set; } = NodeType.Normal;

        // 相邻节点列表
        public List<int> ConnectedNodeIds { get; set; } = new List<int>();

        // 显示标签（用于UI绑定，若属于多节点Zone则显示ZoneName）
        [JsonIgnore]
        public string DisplayLabel { get; set; } = "";
    }
}
