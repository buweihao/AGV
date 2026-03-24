using System;
using System.Collections.Generic;
using System.Linq;
using BasicRegionNavigation.Core.Entities;

namespace BasicRegionNavigation.Common
{
    public static class PathFinder
    {
        private class NodeRecord
        {
            public LogicNode Node;
            public NodeRecord Parent;
            public double G;
            public double F;
        }

        /// <summary>
        /// 基于 A* 算法，从起点目标节点寻找符合地图相连逻辑的最佳短路径。
        /// 返回的路径排除了起点自身，并且按照步进顺序包括终点。
        /// </summary>
        public static List<LogicNode> FindPath(LogicNode startNode, LogicNode targetNode, IEnumerable<LogicNode> allNodes)
        {
            var nodesDict = allNodes.ToDictionary(n => n.Id);
            var openList = new List<NodeRecord>();
            var closedList = new HashSet<int>();

            openList.Add(new NodeRecord { Node = startNode, G = 0, F = GetDistance(startNode, targetNode) });

            while (openList.Count > 0)
            {
                // 获取当前F值最小的节点
                var current = openList.OrderBy(r => r.F).First();

                // 已到达目标点
                if (current.Node.Id == targetNode.Id)
                {
                    return BuildPath(current);
                }

                openList.Remove(current);
                closedList.Add(current.Node.Id);

                // 统一双向检查相邻节点：Node连接了大家，大家也连接了Node
                var neighbors = nodesDict.Values
                    .Where(n => current.Node.ConnectedNodeIds.Contains(n.Id) 
                                || n.ConnectedNodeIds.Contains(current.Node.Id))
                    .ToList();

                foreach (var neighbor in neighbors)
                {
                    if (closedList.Contains(neighbor.Id)) continue; // 如果搜过了就略过

                    double tentativeG = current.G + GetDistance(current.Node, neighbor);

                    var neighborRecord = openList.FirstOrDefault(r => r.Node.Id == neighbor.Id);
                    if (neighborRecord == null)
                    {
                        openList.Add(new NodeRecord
                        {
                            Node = neighbor,
                            Parent = current,
                            G = tentativeG,
                            F = tentativeG + GetDistance(neighbor, targetNode)
                        });
                    }
                    else if (tentativeG < neighborRecord.G)
                    {
                        // 发现有更好的路径到达它
                        neighborRecord.Parent = current;
                        neighborRecord.G = tentativeG;
                        neighborRecord.F = tentativeG + GetDistance(neighbor, targetNode);
                    }
                }
            }

            return new List<LogicNode>(); // 无路可去
        }

        private static double GetDistance(LogicNode a, LogicNode b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static List<LogicNode> BuildPath(NodeRecord endRecord)
        {
            var path = new List<LogicNode>();
            var current = endRecord;
            while (current != null)
            {
                path.Add(current.Node);
                current = current.Parent;
            }
            path.Reverse();

            // 移除起步点自身（因为AGV已经站在起步点了）
            if (path.Count > 0)
            {
                path.RemoveAt(0);
            }
            return path;
        }
    }
}
