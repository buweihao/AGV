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
        /// G 值优先使用 ConnectedNodeDistances 中配置的实际物理长度（米），
        /// 若未配置则降级为坐标欧氏距离作为兜底。
        /// 返回的路径排除了起点自身，并且按照步进顺序包括终点。
        /// </summary>
        public static List<LogicNode> FindPath(LogicNode startNode, LogicNode targetNode, IEnumerable<LogicNode> allNodes, HashSet<int> blockedNodeIds = null)
        {
            var nodesDict = allNodes.ToDictionary(n => n.Id);
            var openList = new List<NodeRecord>();
            var closedList = new HashSet<int>();

            openList.Add(new NodeRecord { Node = startNode, G = 0, F = GetHeuristic(startNode, targetNode) });

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

                // 严格遵守节点自身的连接列表（只能从 current 走向 n）
                var neighbors = nodesDict.Values
                    .Where(n => current.Node.ConnectedNodeIds.Contains(n.Id))
                    .ToList();

                foreach (var neighbor in neighbors)
                {
                    // 【方案 B：动态重寻路】如果该节点在黑名单中（被占领），则寻找替代路径
                    if (blockedNodeIds != null && blockedNodeIds.Contains(neighbor.Id)) continue;

                    if (closedList.Contains(neighbor.Id)) continue; // 如果搜过了就略过

                    // 优先使用配置的实际物理距离；未配置（返回0）则降级用坐标距离
                    double edgeCost = GetEdgeCost(current.Node, neighbor);
                    double tentativeG = current.G + edgeCost;

                    var neighborRecord = openList.FirstOrDefault(r => r.Node.Id == neighbor.Id);
                    if (neighborRecord == null)
                    {
                        openList.Add(new NodeRecord
                        {
                            Node = neighbor,
                            Parent = current,
                            G = tentativeG,
                            F = tentativeG + GetHeuristic(neighbor, targetNode)
                        });
                    }
                    else if (tentativeG < neighborRecord.G)
                    {
                        // 发现有更好的路径到达它
                        neighborRecord.Parent = current;
                        neighborRecord.G = tentativeG;
                        neighborRecord.F = tentativeG + GetHeuristic(neighbor, targetNode);
                    }
                }
            }

            return new List<LogicNode>(); // 无路可去
        }

        /// <summary>
        /// 计算两点之间实际路径的总长度（使用配置的物理距离）。
        /// 如果不可达，则返回 double.MaxValue。
        /// </summary>
        public static double CalculateActualDistance(LogicNode startNode, LogicNode targetNode, IEnumerable<LogicNode> allNodes)
        {
            if (startNode == null || targetNode == null) return double.MaxValue;
            if (startNode.Id == targetNode.Id) return 0;

            var nodesDict = allNodes.ToDictionary(n => n.Id);
            var openList = new List<NodeRecord>();
            var closedList = new HashSet<int>();

            openList.Add(new NodeRecord { Node = startNode, G = 0, F = GetHeuristic(startNode, targetNode) });

            while (openList.Count > 0)
            {
                var current = openList.OrderBy(r => r.F).First();
                if (current.Node.Id == targetNode.Id)
                {
                    return current.G; // G 值即为从起点到当前点的累积实际路线长度
                }

                openList.Remove(current);
                closedList.Add(current.Node.Id);

                var neighbors = nodesDict.Values
                    .Where(n => current.Node.ConnectedNodeIds.Contains(n.Id))
                    .ToList();

                foreach (var neighbor in neighbors)
                {
                    if (closedList.Contains(neighbor.Id)) continue;

                    double edgeCost = GetEdgeCost(current.Node, neighbor);
                    double tentativeG = current.G + edgeCost;
                    var neighborRecord = openList.FirstOrDefault(r => r.Node.Id == neighbor.Id);
                    
                    if (neighborRecord == null)
                    {
                        openList.Add(new NodeRecord
                        {
                            Node = neighbor,
                            Parent = current,
                            G = tentativeG,
                            F = tentativeG + GetHeuristic(neighbor, targetNode)
                        });
                    }
                    else if (tentativeG < neighborRecord.G)
                    {
                        neighborRecord.Parent = current;
                        neighborRecord.G = tentativeG;
                        neighborRecord.F = tentativeG + GetHeuristic(neighbor, targetNode);
                    }
                }
            }

            return double.MaxValue; // 不可达
        }

        /// <summary>
        /// 获取从节点 from 到相邻节点 to 的路段代价。
        /// 优先使用 from.ConnectedNodeDistances[to.Id] 中配置的实际物理距离（单位：米）；
        /// 若未配置（值为0），则降级使用坐标欧氏距离作为兜底。
        /// </summary>
        private static double GetEdgeCost(LogicNode from, LogicNode to)
        {
            double configured = from.GetActualDistance(to.Id);
            return configured > 0 ? configured : GetCoordDistance(from, to);
        }

        /// <summary>
        /// A* 启发式函数（坐标欧氏距离，始终作为乐观估计的 H 值）
        /// </summary>
        private static double GetHeuristic(LogicNode a, LogicNode b)
        {
            return GetCoordDistance(a, b);
        }

        /// <summary>
        /// UI 坐标系欧氏距离（仅作兜底和启发式 H 值）
        /// </summary>
        private static double GetCoordDistance(LogicNode a, LogicNode b)
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

            if (path.Count > 0)
            {
                path.RemoveAt(0); // 排除起始点
            }
            return path;
        }
    }
}
