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
                var idleRobot = _robots.FirstOrDefault(r => r.State == RobotState.IDLE);
                if (idleRobot == null) return; // 没有空闲车辆则退出

                var order = _orderQueue.Dequeue();
                order.AssignedRobotId = idleRobot.Id;
                order.Status = "执行中";

                var node = _mapNodes.FirstOrDefault(n => n.Id == order.TargetNodeId);
                
                if (node != null)
                {
                    _ = ExecuteTaskAsync(idleRobot, node, order);
                }
            }
        }

        private async Task ExecuteTaskAsync(IRobot robot, LogicNode node, TaskOrder order)
        {
            await robot.GoToNodeAsync(node);
            order.Status = "已完成";
            order.IsCompleted = true;
            OnTaskCompleted?.Invoke(order);
        }
    }
}
