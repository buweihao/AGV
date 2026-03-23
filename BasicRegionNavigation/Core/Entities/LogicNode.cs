using System.Collections.Generic;

namespace BasicRegionNavigation.Core.Entities
{
    public class LogicNode
    {
        public int Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        
        // 相邻节点列表
        public List<int> ConnectedNodeIds { get; set; } = new List<int>();
    }
}
