using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Core.Interfaces;
using BasicRegionNavigation.Common;
using TaskStatus = BasicRegionNavigation.Core.Entities.TaskStatus;

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
        
        // 防止空闲车在排队或评估期间被二次派发任务
        private readonly HashSet<string> _dispatchedRobotsCache = new HashSet<string>();

        public event Action<TaskOrder> OnTaskCompleted;

        public TaskDispatcher(List<IRobot> robots, IEnumerable<LogicNode> mapNodes, IDatabaseService databaseService = null)
        {
            _robots = robots;
            _mapNodes = mapNodes;
            _databaseService = databaseService;

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

        /// <summary>
        /// 核心调度逻辑：实现基于欧几里得距离的“就近分配”
        /// </summary>
        private void TryDispatch()
        {
            while (_orderQueue.Count > 0)
            {
                // 1. 预检队列首位任务
                var firstOrder = _orderQueue.Peek();
                var startNode = _mapNodes.FirstOrDefault(n => n.Id == firstOrder.StartNodeId);
                var targetNode = _mapNodes.FirstOrDefault(n => n.Id == firstOrder.TargetNodeId);

                // 异常处理：节点不存在时直接丢弃任务
                if (startNode == null || targetNode == null)
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

                    double dx = currentRobotNode.X - startNode.X;
                    double dy = currentRobotNode.Y - startNode.Y;
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
                order.Status = TaskStatus.PreChecking;

                _ = ExecuteTaskAsync(closestRobot, startNode, targetNode, order);
            }
        }

        /// <summary>
        /// 两段式执行逻辑：去起点取货 -> 去终点卸货 (含联动避让)
        /// </summary>
        private async Task ExecuteTaskAsync(IRobot robot, LogicNode startNode, LogicNode targetNode, TaskOrder order)
        {
            // ====== 任务开始时写入数据库 (状态: 执行中) ======
            var taskHistory = new TaskHistory
            {
                TaskId = order.OrderId,
                RobotId = robot.Id,
                StartNode = startNode.Id,
                EndNode = targetNode.Id,
                CreateTime = DateTime.Now,
                Status = (int)TaskStatus.Executing
            };
            await SafeInsertTaskHistoryAsync(taskHistory);

            try
            {
                // --- 第一阶段：前往起点 (取货) ---
                order.StageDescription = "前往起点";
                order.Status = TaskStatus.Executing;
                await robot.GoToNodeAsync(startNode);

                // 模拟装货动作 (2秒)
                order.StageDescription = "正在装货...";
                await Task.Delay(2000);

                // --- 第二阶段：前往终点 (卸货) ---
                order.StageDescription = "前往终点";

                // 【核心逻辑：终点占用预检 & 联动避让】
                // 确保在迈向终点前，如果终点有人占着，先指挥对方挪窝
                while (true)
                {
                    var occupantRobot = _robots.FirstOrDefault(r => r.Id != robot.Id && r.CurrentNode == targetNode.Id);
                    if (occupantRobot == null)
                    {
                        break; // 终点已空，正常放行
                    }

                    if (occupantRobot.State == RobotState.IDLE)
                    {
                        // 发现占位车闲置，实施强制联动避让
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
                            await Task.Delay(1000); // 无路可退，死等
                        }
                    }
                    else
                    {
                        await Task.Delay(1000); // 占位车正在路过，等待
                    }
                }

                // 避让通过，正式发车前往终位
                await robot.GoToNodeAsync(targetNode);

                // --- 任务结算 ---
                order.Status = TaskStatus.Completed;
                order.StageDescription = string.Empty;
                order.IsCompleted = true;

                // ====== 任务完成时更新数据库 (状态: 已完成) ======
                taskHistory.FinishTime = DateTime.Now;
                taskHistory.Status = (int)TaskStatus.Completed;
                await SafeUpdateTaskHistoryAsync(taskHistory);

                OnTaskCompleted?.Invoke(order);
            }
            catch (Exception ex)
            {
                order.Status = TaskStatus.Fault;
                order.StageDescription = "调度异常: " + ex.Message;

                // ====== 任务异常时更新数据库 (状态: 异常) ======
                taskHistory.FinishTime = DateTime.Now;
                taskHistory.Status = (int)TaskStatus.Fault;
                await SafeUpdateTaskHistoryAsync(taskHistory);
            }
            finally
            {
                // 彻底完成后移出调度锁，触发下一波派发
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
