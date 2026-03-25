using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Core.Interfaces;
using BasicRegionNavigation.Common;
using TaskStatus = BasicRegionNavigation.Core.Entities.TaskStatus;
using static Core.Global;

namespace BasicRegionNavigation.Applications.Dispatchers
{
    /// <summary>
    /// 任务调度引擎：支持“就近分配”与“两段式搬运（取货->卸货）”
    /// </summary>
    public class TaskDispatcher
    {
        private readonly Queue<TaskOrder> _orderQueue = new Queue<TaskOrder>();
        private readonly List<IRobot> _robots;
        private readonly IEnumerable<LogicNode> _mapNodes;
        private readonly IDatabaseService _databaseService; // 【新增】数据库服务，用于持久化任务历史
        private readonly ITrafficController _trafficController;
        
        // 防止空闲车在排队或评估期间被二次派发任务
        private readonly HashSet<string> _dispatchedRobotsCache = new HashSet<string>();

        public event Action<TaskOrder> OnTaskCompleted;

        public TaskDispatcher(List<IRobot> robots, IEnumerable<LogicNode> mapNodes, IDatabaseService databaseService = null, ITrafficController trafficController = null)
        {
            _robots = robots;
            _mapNodes = mapNodes;
            _databaseService = databaseService;
            _trafficController = trafficController;

            foreach (var robot in _robots)
            {
                robot.OnRobotStateChanged += HandleRobotStateChanged;
                // 【新增】订阅低电量事件 (事件驱动架构)
                robot.OnBatteryLow += OnRobotBatteryLow;
            }

            // 【步骤二：启动现场恢复（盲采上锁）】
            if (_trafficController != null)
            {
                foreach (var robot in _robots)
                {
                    var node = _mapNodes.FirstOrDefault(n => n.Id == robot.CurrentNode);
                    if (node != null)
                    {
                        // 盲采上锁：不判断是否占用，直接强制锁定物理身下的领土
                        string zoneName = GetZoneName(node);
                        _trafficController.ForceAcquireLock(zoneName, robot.Id);
                    }
                }
            }
        }

        private void HandleRobotStateChanged(RobotState state)
        {
            if (state == RobotState.IDLE)
            {
                TryDispatch();
            }
        }

        /// <summary>
        /// 低电量事件处理逻辑
        /// </summary>
        private void OnRobotBatteryLow(IRobot robot)
        {
            // 如果小车当前正在忙碌，暂时不中断，等它 IDLE 后再触发（或者由 TryDispatch 捕获）
            // 如果已经是 IDLE 状态，且没有被预占，则立刻下发回充任务
            if (robot.State == RobotState.IDLE && !_dispatchedRobotsCache.Contains(robot.Id))
            {
                // 1. 寻找最近的充电节点
                var chargingNode = _mapNodes
                    .Where(n => n.NodeType == "Charging")
                    .OrderBy(n => Math.Sqrt(Math.Pow(n.X - robot.CurrentX, 2) + Math.Pow(n.Y - robot.CurrentY, 2)))
                    .FirstOrDefault();

                if (chargingNode != null)
                {
                    // 2. 如果已经身在充电节点，或者就在充电节点旁边，也需要补电（由 MockRobot 内部处理）
                    // 3. 生成紧急回充任务（通过 prefix 标识）
                    var chargeOrder = new TaskOrder 
                    { 
                        OrderId = "CHARGE-" + Guid.NewGuid().ToString().Substring(0, 4),
                        StageDescription = "低电量自动回充"
                    };
                    chargeOrder.Stages.Enqueue(new TaskStage { TargetNodeId = chargingNode.Id, WaitTimeMs = 0, StageName = "前往快速补电" });

                    Serilog.Log.Information($"[自动回充] 侦测到小车 {robot.Id} 电量低 ({robot.BatteryLevel:F1}%)，自动下发任务至充电桩 {chargingNode.Id}");
                    SubmitTask(chargeOrder);
                }
            }
        }

        public void SubmitTask(TaskOrder order)
        {
            _orderQueue.Enqueue(order);
            TryDispatch();
        }

        /// <summary>
        /// 核心调度逻辑：实现基于欧几里得距离的“就近分配”
        /// </summary>
        private void TryDispatch()
        {
            while (_orderQueue.Count > 0)
            {
                // 1. 预检队列首位任务
                var firstOrder = _orderQueue.Peek();
                if (firstOrder.Stages == null || firstOrder.Stages.Count == 0)
                {
                    _orderQueue.Dequeue();
                    continue;
                }

                var firstStage = firstOrder.Stages.Peek();
                var evalNode = _mapNodes.FirstOrDefault(n => n.Id == firstStage.TargetNodeId);

                // 异常处理：节点不存在时直接丢弃任务
                if (evalNode == null)
                {
                    _orderQueue.Dequeue();
                    continue;
                }

                // 2. 寻找当前真正空闲且未被预占的小车
                var availableRobots = _robots
                    .Where(r => r.State == RobotState.IDLE && !_dispatchedRobotsCache.Contains(r.Id))
                    .ToList();
                
                if (availableRobots.Count == 0) return; 

                // 3. 【算法实现】就近分配 (勾股定理计算直线距离)
                IRobot closestRobot = null;
                double minDistance = double.MaxValue;

                foreach (var robot in availableRobots)
                {
                    // 获取小车当前所在节点的位置信息
                    var currentRobotNode = _mapNodes.FirstOrDefault(n => n.Id == robot.CurrentNode);
                    if (currentRobotNode == null) continue;

                    double dx = currentRobotNode.X - evalNode.X;
                    double dy = currentRobotNode.Y - evalNode.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestRobot = robot;
                    }
                }

                if (closestRobot == null) return;

                // 4. 确定分配：出队并加入缓存执行
                var order = _orderQueue.Dequeue();
                _dispatchedRobotsCache.Add(closestRobot.Id);
                
                order.AssignedRobotId = closestRobot.Id;
                order.Status = TaskStatus.Executing;

                _ = ExecuteTaskAsync(closestRobot, order);
            }
        }

        /// <summary>
        /// 多段任务流执行逻辑：按顺序依次执行所有 Stage
        /// </summary>
        private async Task ExecuteTaskAsync(IRobot robot, TaskOrder order)
        {
            // ====== 任务开始时写入数据库 (状态: 执行中) ======
            var firstNode = _mapNodes.FirstOrDefault(n => n.Id == (order.Stages.Peek()?.TargetNodeId ?? 0));
            var taskHistory = new TaskHistory
            {
                TaskId = order.OrderId,
                RobotId = robot.Id,
                StartNode = robot.CurrentNode,
                EndNode = firstNode?.Id ?? 0,
                CreateTime = DateTime.Now,
                Status = (int)TaskStatus.Executing
            };
            await SafeInsertTaskHistoryAsync(taskHistory);

            try
            {
                while (order.Stages.Count > 0)
                {
                    var currentStage = order.Stages.Dequeue();
                    var targetNode = _mapNodes.FirstOrDefault(n => n.Id == currentStage.TargetNodeId);
                    
                    if (targetNode == null) continue;

                    order.StageDescription = currentStage.StageName;
                    
                    // 【核心逻辑：终点占用预检 & 联动避让】
                    // 确保在迈向阶段目标点前，清理终点占位车
                    while (true)
                    {
                        var occupantRobot = _robots.FirstOrDefault(r => r.Id != robot.Id && r.CurrentNode == targetNode.Id);
                        if (occupantRobot == null) break;

                        if (occupantRobot.State == RobotState.IDLE)
                        {
                            var bufferCandidates = _mapNodes
                                .Where(n => (targetNode.ConnectedNodeIds.Contains(n.Id) || n.ConnectedNodeIds.Contains(targetNode.Id))
                                            && !_robots.Any(r => r.CurrentNode == n.Id))
                                .ToList();

                            var bufferNode = bufferCandidates.FirstOrDefault(n => n.IsBufferNode) ?? bufferCandidates.FirstOrDefault();

                            if (bufferNode != null)
                            {
                                await occupantRobot.GoToNodeAsync(bufferNode);
                            }
                            else
                            {
                                await Task.Delay(1000); 
                            }
                        }
                        else
                        {
                            await Task.Delay(1000); 
                        }
                    }

                    // 执行移动
                    await robot.GoToNodeAsync(targetNode);

                    // 到达后的动作停留
                    if (currentStage.WaitTimeMs > 0)
                    {
                        order.StageDescription = $"{currentStage.StageName} (等待 {currentStage.WaitTimeMs}ms)";
                        await Task.Delay(currentStage.WaitTimeMs);
                    }
                }

                // --- 全部阶段完成 ---
                order.Status = TaskStatus.Completed;
                order.StageDescription = string.Empty;
                order.IsCompleted = true;

                taskHistory.FinishTime = DateTime.Now;
                taskHistory.Status = (int)TaskStatus.Completed;
                await SafeUpdateTaskHistoryAsync(taskHistory);

                OnTaskCompleted?.Invoke(order);
            }
            catch (Exception ex)
            {
                order.Status = TaskStatus.Fault;
                order.StageDescription = "派单执行故障: " + ex.Message;

                taskHistory.FinishTime = DateTime.Now;
                taskHistory.Status = (int)TaskStatus.Fault;
                await SafeUpdateTaskHistoryAsync(taskHistory);
            }
            finally
            {
                _dispatchedRobotsCache.Remove(robot.Id);
                TryDispatch();
            }
        }

        /// <summary>
        /// 安全地写入任务历史 (带空检查和异常兜底，避免数据库问题影响调度主流程)
        /// </summary>
        private async Task SafeInsertTaskHistoryAsync(TaskHistory history)
        {
            if (_databaseService == null) return;
            try
            {
                await _databaseService.InsertTaskHistoryAsync(history);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskDispatcher] 数据库插入失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全地更新任务历史 (用于任务完成/异常时回写状态)
        /// </summary>
        private async Task SafeUpdateTaskHistoryAsync(TaskHistory history)
        {
            if (_databaseService == null) return;
            try
            {
                await _databaseService.UpdateTaskHistoryAsync(history);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TaskDispatcher] 数据库更新失败: {ex.Message}");
            }
        }
    }
}
