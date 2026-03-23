using System;
using System.Threading.Tasks;
using BasicRegionNavigation.Core.Interfaces;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Common;

namespace BasicRegionNavigation.Infrastructure.Robots
{
    public class MockRobot : IRobot
    {
        public string Id { get; }
        public int CurrentNode { get; set; }
        public double CurrentX { get; set; }
        public double CurrentY { get; set; }
        public RobotState State { get; private set; } = RobotState.IDLE;

        public event Action<double, double> OnPositionChanged;
        public event Action<int> OnNodeChanged;
        public event Action<RobotState> OnStateChanged;

        private Action<RobotState> _onStateUpdate;
        private Action<string> _onError;
        private bool _cancelFlag;
        private readonly ITrafficController _trafficController;

        public MockRobot(string id, ITrafficController trafficController, Action<RobotState> onStateUpdate = null, Action<string> onError = null)
        {
            Id = id;
            _trafficController = trafficController;
            _onStateUpdate = onStateUpdate;
            _onError = onError;
            CurrentX = 0;
            CurrentY = 0;
            CurrentNode = 0;
            SetState(RobotState.IDLE);
        }

        private void SetState(RobotState state)
        {
            State = state;
            _onStateUpdate?.Invoke(state);
            OnStateChanged?.Invoke(state);
        }

        public async Task GoToNodeAsync(LogicNode targetNode)
        {
            if (State == RobotState.ERROR)
            {
                Console.WriteLine("MockRobot: 当前处于故障状态，须先重置机器。");
                return;
            }
            if (State != RobotState.IDLE)
            {
                Cancel();
                await Task.Delay(150);
            }

            // 【关键修复】在等待获取锁之前，先将状态设为 MOVING 以避免并发派单导致同一个空闲车被多次抢占
            SetState(RobotState.MOVING);
            _cancelFlag = false;

            Console.WriteLine($"MockRobot {Id}: 开始向节点 {targetNode.Id} ({targetNode.X}, {targetNode.Y}) 移动...");
            
            // 请求目标节点的锁（新增超时捕获打破死锁）
            try
            {
                await _trafficController.WaitAndAcquireLockAsync(targetNode.Id, this.Id);
            }
            catch (System.TimeoutException ex)
            {
                Console.WriteLine($"MockRobot {Id}: 发生死锁异常 - {ex.Message}");
                _cancelFlag = true;
                SetState(RobotState.ERROR);
                _onError?.Invoke("调度死锁，已自动取消任务");
                return;
            }
            
            double dx = targetNode.X - CurrentX;
            double dy = targetNode.Y - CurrentY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            
            if (distance > 0)
            {
                // 每次移动 5 个像素
                double steps = distance / 5.0;
                double stepX = dx / steps;
                double stepY = dy / steps;
                
                int totalSteps = (int)Math.Ceiling(steps);
                
                for (int i = 0; i < totalSteps; i++)
                {
                    while (State == RobotState.PAUSED)
                    {
                        if (_cancelFlag) break;
                        await Task.Delay(50);
                    }
                    if (_cancelFlag) break;

                    if (new Random().NextDouble() < 0.001)
                    {
                        _cancelFlag = true;
                        SetState(RobotState.ERROR);
                        _onError?.Invoke("Obstacle Detected");
                        break;
                    }

                    CurrentX += stepX;
                    CurrentY += stepY;
                    OnPositionChanged?.Invoke(CurrentX, CurrentY);
                    
                    await Task.Delay(50);
                }
            }

            if (!_cancelFlag)
            {
                CurrentX = targetNode.X;
                CurrentY = targetNode.Y;
                int previousNodeId = CurrentNode;
                CurrentNode = targetNode.Id;

                OnPositionChanged?.Invoke(CurrentX, CurrentY);
                OnNodeChanged?.Invoke(CurrentNode);

                // 【关键修复】先释放旧锁、打印日志，然后再通知外部状态变为 IDLE！
                // 这样能杜绝倒装日志以及上层调度器过早插入新任务导致的时序混乱！
                if (previousNodeId != CurrentNode) 
                {
                    _trafficController.ReleaseLock(previousNodeId, this.Id);
                }
                
                Console.WriteLine($"MockRobot {Id}: 已到达节点 {CurrentNode}。");
                
                SetState(RobotState.IDLE);
            }
            else
            {
                // 【修复：幽灵死锁漏洞】
                // 车辆在移动途中发生错误（ERROR）或被强行取消时，它并没有抵达 targetNode。
                // 但在派单起步时，它已经提前锁死了 targetNode，如果不释放，那个节点将永久变成不可通行的“幽灵死锁”节点！
                // 释放掉未完成的终点锁，只保留当前由于它其实还停留在原地（或路上），所以保留住它原本的 CurrentNode 锁是合理的。
                if (targetNode.Id != CurrentNode)
                {
                    _trafficController.ReleaseLock(targetNode.Id, this.Id);
                }
            }
        }

        public void Pause()
        {
            if (State == RobotState.MOVING) SetState(RobotState.PAUSED);
        }
        public void Resume()
        {
            if (State == RobotState.PAUSED) SetState(RobotState.MOVING);
        }
        public void Cancel()
        {
            if (State != RobotState.IDLE && State != RobotState.ERROR)
            {
                _cancelFlag = true;
                SetState(RobotState.IDLE);
                Console.WriteLine("MockRobot: 当前任务已被取消。");
            }
        }
        public void Reset()
        {
            if (State == RobotState.ERROR)
            {
                SetState(RobotState.IDLE);
                Console.WriteLine("MockRobot: 已重置并恢复 IDLE 状态。");
            }
        }
    }
}
