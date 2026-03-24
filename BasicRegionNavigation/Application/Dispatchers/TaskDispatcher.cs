using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Core.Interfaces;
using BasicRegionNavigation.Common;

namespace BasicRegionNavigation.Applications.Dispatchers
{
    public class TaskDispatcher
    {
        private readonly Queue<TaskOrder> _orderQueue = new Queue<TaskOrder>();
        private readonly List<IRobot> _robots;
        private readonly IEnumerable<LogicNode> _mapNodes;
        
        // 防止空闲车在排队或评估终点时被二次派发任务
        private readonly HashSet<string> _dispatchedRobotsCache = new HashSet<string>();

        public event Action<TaskOrder> OnTaskCompleted;

        public TaskDispatcher(List<IRobot> robots, IEnumerable<LogicNode> mapNodes)
        {
            _robots = robots;
            _mapNodes = mapNodes;

            foreach (var robot in _robots)
            {
                robot.OnStateChanged += OnRobotStateChanged;
            }
        }

        private void OnRobotStateChanged(RobotState state)
        {
            if (state == RobotState.IDLE)
            {
                TryDispatch();
            }
        }

        public void SubmitTask(TaskOrder order)
        {
            _orderQueue.Enqueue(order);
            TryDispatch();
        }

        private void TryDispatch()
        {
            while (_orderQueue.Count > 0)
            {
                // 筛选空闲且没有在处理预检验任务的小车
                var idleRobot = _robots.FirstOrDefault(r => r.State == RobotState.IDLE && !_dispatchedRobotsCache.Contains(r.Id));
                if (idleRobot == null) return; 

                _dispatchedRobotsCache.Add(idleRobot.Id);

                var order = _orderQueue.Dequeue();
                order.AssignedRobotId = idleRobot.Id;
                order.Status = "预检避让处理中";

                var node = _mapNodes.FirstOrDefault(n => n.Id == order.TargetNodeId);
                
                if (node != null)
                {
                    _ = ExecuteTaskAsync(idleRobot, node, order);
                }
                else
                {
                    _dispatchedRobotsCache.Remove(idleRobot.Id);
                }
            }
        }

        private async Task ExecuteTaskAsync(IRobot robot, LogicNode node, TaskOrder order)
        {
            // ====== 终点占用预检 (Pre-check) & 联动避让 (Coordinated Evasion) ======
            while (true)
            {
                var occupantRobot = _robots.FirstOrDefault(r => r.Id != robot.Id && r.CurrentNode == node.Id);
                if (occupantRobot == null)
                {
                    break; // 终点空闲，正常放行
                }

                if (occupantRobot.State == RobotState.IDLE)
                {
                    Console.WriteLine($"[调度引擎] 发现 {occupantRobot.Id} 闲置占用了 {robot.Id} 的终点节点 {node.Id}，实施联动避让...");
                    
                    // 寻找此终点的空闲相邻节点作为避让停车湾 (Buffer Node)
                    var bufferCandidates = _mapNodes
                        .Where(n => (node.ConnectedNodeIds.Contains(n.Id) || n.ConnectedNodeIds.Contains(node.Id))
                                    && !_robots.Any(r => r.CurrentNode == n.Id))
                        .ToList();

                    // 优先选择专职的 IsBufferNode
                    var bufferNode = bufferCandidates.FirstOrDefault(n => n.IsBufferNode);
                    if (bufferNode == null)
                    {
                        bufferNode = bufferCandidates.FirstOrDefault(); // 退而求其次选择普通空闲节点
                    }

                    if (bufferNode != null)
                    {
                        Console.WriteLine($"[联动避让] 强行指挥 {occupantRobot.Id} 挪窝到避让点 {bufferNode.Id}...");
                        // 强制调离，这个动作不生成对用户的可见 TaskOrder
                        // 这里我们使用 await 确保占用车完成避让后，才释放死等循环
                        await occupantRobot.GoToNodeAsync(bufferNode);
                    }
                    else
                    {
                        Console.WriteLine($"[联动避让警告] {occupantRobot.Id} 四周无路可退，{robot.Id} 只能在起点痛苦等待...");
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    Console.WriteLine($"[占用等待] 终点节点 {node.Id} 正被 {occupantRobot.Id} 动态路过(状态:{occupantRobot.State})，等待其离开...");
                    await Task.Delay(1000);
                }
            }

            // ====== 正式放行 ======
            order.Status = "执行中";
            await robot.GoToNodeAsync(node);
            
            order.Status = "已完成";
            order.IsCompleted = true;
            OnTaskCompleted?.Invoke(order);

            // 任务彻底执行完成离开管辖，移出预分配缓存
            _dispatchedRobotsCache.Remove(robot.Id);
            
            // 顺便唤醒后续可能的派单
            TryDispatch();
        }
    }
}
