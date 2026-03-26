using BasicRegionNavigation.Common;
using System.Collections.Generic;

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
        public NodeType NodeType { get; set; } = NodeType.Normal;

        // 相邻节点列表
        public List<int> ConnectedNodeIds { get; set; } = new List<int>();
    }
}
