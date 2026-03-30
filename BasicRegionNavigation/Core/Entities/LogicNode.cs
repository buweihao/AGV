using BasicRegionNavigation.Common;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// 相邻节点及实际物理距离字典。
        /// Key = 相连目标节点ID，Value = 实际路段物理长度（单位：米）。
        /// JSON 格式示例：{ "2": 5.5, "3": 12.0 }
        /// 若未配置则默认 0，兜底逻辑会自动降级为坐标欧氏距离。
        /// </summary>
        public Dictionary<int, double> ConnectedNodeDistances { get; set; } = new Dictionary<int, double>();

        /// <summary>
        /// 向下兼容的只读属性，返回所有相连节点 ID 集合（即字典的 Keys）。
        /// 所有原先使用 ConnectedNodeIds 的代码无需修改。
        /// </summary>
        [JsonIgnore]
        public ICollection<int> ConnectedNodeIds => ConnectedNodeDistances.Keys;

        /// <summary>
        /// 获取到相邻节点 neighborId 的实际物理距离（米）。
        /// 若字典中未配置，则返回 0（调用方应降级使用坐标距离）。
        /// </summary>
        public double GetActualDistance(int neighborId)
        {
            return ConnectedNodeDistances.TryGetValue(neighborId, out double dist) ? dist : 0;
        }

        // 显示标签（用于UI绑定，若属于多节点Zone则显示ZoneName）
        [JsonIgnore]
        public string DisplayLabel { get; set; } = "";
    }
}
