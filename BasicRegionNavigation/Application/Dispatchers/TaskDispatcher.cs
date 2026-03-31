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
        
        // 【新增】防重分配缓存：存储已静态或动态预占的节点（配合动态终点）
        private readonly HashSet<int> _reservedNodesCache = new HashSet<int>();

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
                foreach (var robot in _robots.Where(r => r.State == RobotState.IDLE && r.BatteryLevel <= 20 && !_dispatchedRobotsCache.Contains(r.Id)))
                {
                    OnRobotBatteryLow(robot);
                }
                TryDispatch();
            }
        }

        /// <summary>
        /// 低电量事件处理逻辑
        /// </summary>
        private void OnRobotBatteryLow(IRobot robot)
        {
            if (robot.State == RobotState.IDLE && !_dispatchedRobotsCache.Contains(robot.Id))
            {
                var chargeOrder = new TaskOrder 
                { 
                    OrderId = "CHARGE-" + Guid.NewGuid().ToString().Substring(0, 4),
                    StageDescription = "低电量自动回充"
                };
                
                // 【关键修复】：不手动算距离，直接使用动态寻址 (DynamicTargetType = "Charging")
                // 调度器的 ResolveTargetNodeAsync 会自动寻找最近且未被占用的充电桩，并加上防重占锁
                chargeOrder.Stages.Enqueue(new TaskStage { 
                    TargetNodeId = 0, 
                    DynamicTargetType = "Charging", 
                    WaitTimeMs = 0, 
                    StageName = "前往快速补电" 
                });

                _dispatchedRobotsCache.Add(robot.Id);
                chargeOrder.AssignedRobotId = robot.Id;
                chargeOrder.Status = TaskStatus.Executing;
                
                Serilog.Log.Information($"[自动回充] 侦测到小车 {robot.Id} 电量低，已下发动态回充任务。");
                _ = ExecuteTaskAsync(robot, chargeOrder);
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
                LogicNode evalNode = null;

                // 【修复】兼容首个阶段是动态寻址 (TargetNodeId = 0) 的情况
                if (firstStage.TargetNodeId == 0 && !string.IsNullOrEmpty(firstStage.DynamicTargetType))
                {
                    if (firstStage.CandidateNodeIds != null && firstStage.CandidateNodeIds.Count > 0)
                    {
                        // 如果有候选节点，取第一个候选节点用于距离计算的基准
                        evalNode = _mapNodes.FirstOrDefault(n => n.Id == firstStage.CandidateNodeIds[0]);
                    }
                    else
                    {
                        // 如果没有指定候选节点列表，随便找一个同类型的节点作为距离评估基准
                        evalNode = _mapNodes.FirstOrDefault(n => n.NodeType.ToString() == firstStage.DynamicTargetType);
                    }
                }
                else
                {
                    // 正常的静态目标节点
                    evalNode = _mapNodes.FirstOrDefault(n => n.Id == firstStage.TargetNodeId);
                }

                // 异常处理：节点确实不存在时才丢弃任务
                if (evalNode == null)
                {
                    Serilog.Log.Error($"[调度计算] 任务 {firstOrder.OrderId} 找不到有效的评估节点，已从队列中丢弃。");
                    _orderQueue.Dequeue();
                    continue;
                }

                // ... 下方继续保留你原本的寻找空闲车辆、计算 A* 实际距离并分配任务的代码
                var availableRobots = _robots
                    .Where(r => r.State == RobotState.IDLE && r.BatteryLevel > 20 && !_dispatchedRobotsCache.Contains(r.Id))
                    .ToList();

                if (availableRobots.Count == 0) return; 

                // 3. 【算法实现】就近分配 (勾股定理计算直线距离)
                IRobot closestRobot = null;
                double minDistance = double.MaxValue;

                Serilog.Log.Debug($"[调度计算] 正在为任务 {firstOrder.OrderId} 挑选最近空闲车辆 (目标节点: {firstStage.TargetNodeId})");

                foreach (var robot in availableRobots)
                {
                    // 获取小车当前所在节点的位置信息
                    var currentRobotNode = _mapNodes.FirstOrDefault(n => n.Id == robot.CurrentNode);
                    if (currentRobotNode == null)
                    {
                        Serilog.Log.Warning($"[调度计算] 车辆 {robot.Id} 当前所在节点 {robot.CurrentNode} 在地图中不存在，跳过。");
                        continue;
                    }

                    // double dx = currentRobotNode.X - evalNode.X;
                    // double dy = currentRobotNode.Y - evalNode.Y;
                    // double distance = Math.Sqrt(dx * dx + dy * dy);

                    // 【核心修改】使用 A* 计算实际路网距离，而不是直线距离
                    double distance = BasicRegionNavigation.Common.PathFinder.CalculateActualDistance(currentRobotNode, evalNode, _mapNodes);

                    Serilog.Log.Debug($"[调度计算] 车辆 {robot.Id} (节点:{robot.CurrentNode}) 到目标节点 {firstStage.TargetNodeId} 的路网实际路线距离为: {distance:F1}");

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestRobot = robot;
                    }
                }

                if (closestRobot == null)
                {
                    Serilog.Log.Warning($"[调度计算] 任务 {firstOrder.OrderId} 找不到可计算距离的车辆，暂时无法派发。");
                    return;
                }

                Serilog.Log.Information($"[调度计算] 匹配成功！任务 {firstOrder.OrderId} 分配给最近车辆: {closestRobot.Id} (距离:{minDistance:F1})");

                // 4. 确定分配：出队并加入缓存执行
                var order = _orderQueue.Dequeue();
                _dispatchedRobotsCache.Add(closestRobot.Id);
                
                order.AssignedRobotId = closestRobot.Id;
                order.Status = TaskStatus.Executing;

                _ = ExecuteTaskAsync(closestRobot, order, minDistance);
            }
        }

        /// <summary>
        /// 多段任务流执行逻辑：按顺序依次执行所有 Stage
        /// </summary>
        private async Task ExecuteTaskAsync(IRobot robot, TaskOrder order, double priorityDistance = 0)
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

            var reservedNodesThisOrder = new List<int>(); // 【新增】跟踪该订单的动态保留

            try
            {
                while (order.Stages.Count > 0)
                {
                    var currentStage = order.Stages.Dequeue();

                    // 步骤 A：解析目标节点 (动态寻址与静态预检)
                    var targetNode = await ResolveTargetNodeAsync(robot, currentStage, reservedNodesThisOrder);
                    
                    order.StageDescription = currentStage.StageName;
                    
                    // 步骤 B：申请交通控制锁 (自带超时机制)
                    if (_trafficController != null)
                    {
                        robot.RecordStop($"正在申请节点 {targetNode.Id} 的通行证...");
                        // 获取对应的区域名称（ZoneName），实现多节点对应单锁
                        string targetZone = GetZoneName(targetNode);
                        await _trafficController.WaitAndAcquireLockAsync(targetZone, robot.Id, priorityDistance);
                    }

                    try 
                    {
                        // 步骤 C：物理占用清理 (驱赶停在目标点的空闲车辆)
                        await CheckAndClearPhysicalOccupancyAsync(robot, targetNode);

                        // 物理移动
                        await robot.GoToNodeAsync(targetNode);

                        // 执行业务动作
                        if (!string.IsNullOrEmpty(currentStage.ActionCode) && currentStage.ActionCode != "None")
                        {
                            order.StageDescription = $"{currentStage.StageName} (执行: {currentStage.ActionCode})";
                            await ExecuteStageActionAsync(currentStage);
                        }
                        else if (currentStage.WaitTimeMs > 0)
                        {
                            order.StageDescription = $"{currentStage.StageName} (等待 {currentStage.WaitTimeMs}ms)";
                            await Task.Delay(currentStage.WaitTimeMs);
                        }
                    }
                    finally
                    {
                        // 步骤 D：释放节点和交通锁
                        ReleaseStageLocks(targetNode.Id, robot.Id, reservedNodesThisOrder);
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
                // 1. 先解除小车的占用状态
                _dispatchedRobotsCache.Remove(robot.Id);

                // 2. 【新增】任务彻底结束后，立刻检查电量。如果已经是低电量，主动触发回充！
                if (robot.BatteryLevel <= 20)
                {
                    Serilog.Log.Information($"[任务结束检查] 小车 {robot.Id} 刚完成任务，当前电量极低 ({robot.BatteryLevel:F1}%)，立即转入回充流程。");
                    // 由于已经移除了占用，且小车处于 IDLE 状态，调用此方法将成功派发充电任务
                    OnRobotBatteryLow(robot);
                }

                // 3. 尝试调度其他任务（不用担心低电量车会接单，因为 TryDispatch 过滤了电量 > 20）
                TryDispatch();
            }
        }

        private async Task<LogicNode> ResolveTargetNodeAsync(IRobot robot, TaskStage stage, List<int> reservedNodesThisOrder)
        {
            if (stage.TargetNodeId == 0 && !string.IsNullOrEmpty(stage.DynamicTargetType))
            {
                while (true)
                {
                    var currentRobotNode = _mapNodes.FirstOrDefault(n => n.Id == robot.CurrentNode);
                    var candidates = _mapNodes.Where(n =>
                        n.NodeType.ToString() == stage.DynamicTargetType &&
                        !_reservedNodesCache.Contains(n.Id) &&
                        !_robots.Any(r => r.CurrentNode == n.Id)).ToList();

                    if (stage.CandidateNodeIds != null && stage.CandidateNodeIds.Count > 0)
                    {
                        candidates = candidates.Where(n => stage.CandidateNodeIds.Contains(n.Id)).ToList();
                    }

                    if (candidates.Any())
                    {
                        LogicNode bestNode = null;
                        double minDistance = double.MaxValue;

                        foreach (var node in candidates)
                        {
                            double dist = BasicRegionNavigation.Common.PathFinder.CalculateActualDistance(currentRobotNode, node, _mapNodes);
                            if (dist < minDistance)
                            {
                                minDistance = dist;
                                bestNode = node;
                            }
                        }

                        if (bestNode != null)
                        {
                            stage.TargetNodeId = bestNode.Id;
                            _reservedNodesCache.Add(bestNode.Id);
                            reservedNodesThisOrder.Add(bestNode.Id);
                            Serilog.Log.Information($"[动态寻址] 为车 {robot.Id} 匹配最优目的地: 节点 {bestNode.Id} ({stage.DynamicTargetType})");
                            break;
                        }
                    }

                    Serilog.Log.Warning($"[动态寻址] 小车 {robot.Id} 无可用动态目标 ({stage.DynamicTargetType})，等待 1 秒尝试...");
                    await Task.Delay(1000);
                }
            }

            var targetNode = _mapNodes.FirstOrDefault(n => n.Id == stage.TargetNodeId);
            
            if (targetNode == null) 
            {
                throw new Exception($"调度异常：阶段 [{stage.StageName}] 找不到有效的目标节点，请检查动态寻址配置。");
            }
            
            return targetNode;
        }

        private async Task CheckAndClearPhysicalOccupancyAsync(IRobot currentRobot, LogicNode targetNode)
        {
            int maxWaitSeconds = 30; // 最大等待30秒
            int waited = 0;

            while (true)
            {
                var occupantRobot = _robots.FirstOrDefault(r => r.Id != currentRobot.Id && r.CurrentNode == targetNode.Id);
                if (occupantRobot == null) break;

                // 【新增日志】死锁预警：当前节点被物理占用
                Serilog.Log.Warning($"[死锁预警] 车辆 {currentRobot.Id} 无法进入节点 {targetNode.Id}，该点被车辆 {occupantRobot.Id} (状态: {occupantRobot.State}) 物理占用。");

                if (waited >= maxWaitSeconds)
                {
                    Serilog.Log.Error($"[死锁警告] 车 {currentRobot.Id} 试图驱赶目标节点 {targetNode.Id} 上的车 {occupantRobot.Id}，但等待 {maxWaitSeconds}s 失败！强行中止驱赶。");
                    throw new Exception("驱赶目标点车辆超时，路网死锁"); // 抛出异常让外层捕获，任务转为 Fault 状态并释放锁
                }

                if (occupantRobot.State == RobotState.IDLE)
                {
                    var bufferCandidates = _mapNodes
                        .Where(n => (targetNode.ConnectedNodeIds.Contains(n.Id) || n.ConnectedNodeIds.Contains(targetNode.Id))
                                    && !_robots.Any(r => r.CurrentNode == n.Id))
                        .ToList();

                    var bufferNode = bufferCandidates.FirstOrDefault(n => n.IsBufferNode) ?? bufferCandidates.FirstOrDefault();

                    if (bufferNode != null)
                    {
                        Serilog.Log.Information($"[路权管理] 正在将车 {occupantRobot.Id} 驱离至缓冲节点 {bufferNode.Id}...");
                        await occupantRobot.GoToNodeAsync(bufferNode);
                    }
                    else
                    {
                        await Task.Delay(1000); 
                        waited++;
                    }
                }
                else
                {
                    // 占用车不是 IDLE（可能故障了），无法驱赶
                    await Task.Delay(1000); 
                    waited++;
                }
            }
        }

        private void ReleaseStageLocks(int targetNodeId, string robotId, List<int> reservedNodesThisOrder)
        {
            if (reservedNodesThisOrder.Contains(targetNodeId))
            {
                _reservedNodesCache.Remove(targetNodeId);
                reservedNodesThisOrder.Remove(targetNodeId);
            }

            if (_trafficController != null)
            {
                // 先查找到映射节点，获取正确的 ZoneName 进行锁释放
                var node = _mapNodes.FirstOrDefault(n => n.Id == targetNodeId);
                string zoneName = node != null ? GetZoneName(node) : targetNodeId.ToString();
                
                _trafficController.ReleaseLock(zoneName, robotId);
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

        /// <summary>
        /// 模拟执行具体的业务动作（如 PLC 握手、装卸料）
        /// </summary>
        private async Task ExecuteStageActionAsync(TaskStage stage)
        {
            Serilog.Log.Information($"[动作开始] 执行业务码: {stage.ActionCode}, 预计耗时: {stage.WaitTimeMs}ms");

            switch (stage.ActionCode)
            {
                case "Plc_Load":
                    // 模拟 PLC 装料逻辑
                    await Task.Delay(stage.WaitTimeMs);
                    Serilog.Log.Information("[动作完成] PLC 装料成功。");
                    break;

                case "Plc_Unload":
                    // 模拟 PLC 卸料逻辑
                    await Task.Delay(stage.WaitTimeMs);
                    Serilog.Log.Information("[动作完成] PLC 卸料成功。");
                    break;

                case "Mock_Docking":
                    // 模拟充电桩对接
                    await Task.Delay(2000);
                    break;

                default:
                    // 默认延时兜底
                    if (stage.WaitTimeMs > 0) await Task.Delay(stage.WaitTimeMs);
                    break;
            }
        }

        public void UnsubscribeFromRobots()
        {
            if (_robots == null) return;
            foreach (var robot in _robots)
            {
                robot.OnRobotStateChanged -= HandleRobotStateChanged;
                robot.OnBatteryLow -= OnRobotBatteryLow;
            }
        }
    }
}
