using BasicRegionNavigation.Common;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Core.Interfaces;
using Core;
using System;
using System.Threading.Tasks;

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

            // 获取起止点的交通管制区 ZoneId
            int currentZoneId = Global.GetZoneId(CurrentNode);
            int targetZoneId = Global.GetZoneId(targetNode.Id);

            if (currentZoneId != targetZoneId)
            {
                Console.WriteLine($"MockRobot {Id}: 准备跨区，正在申请目标管制区 Zone {targetZoneId} 的锁...");
                // 请求目标管制区的锁（新增超时捕获打破死锁）
                try
                {
                    await _trafficController.WaitAndAcquireLockAsync(targetZoneId, this.Id);
                }
                catch (BasicRegionNavigation.Applications.Controllers.ZoneLockTimeoutException ex)
                {
                    Console.WriteLine($"MockRobot { Id}: 发生系统级管制区超时死锁异常 - {ex.Message}");
                    _cancelFlag = true;
                    SetState(RobotState.ERROR);
                    _onError?.Invoke("通信或物理占区超时(系统级严重死锁)，已抛出异常并取消任务");
                    return;
                }
            }
            else
            {
                Console.WriteLine($"MockRobot {Id}: 同区移动 (Zone {currentZoneId})，无需重复申请交通锁。");
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
                CurrentNode = targetNode.Id;

                OnPositionChanged?.Invoke(CurrentX, CurrentY);
                OnNodeChanged?.Invoke(CurrentNode);

                // 【重构修复】到达目标节点后，检查是否跨区。若是跨区，才释放旧管制区的锁。
                if (currentZoneId != targetZoneId) 
                {
                    _trafficController.ReleaseLock(currentZoneId, this.Id);
                    Console.WriteLine($"MockRobot {Id}: 已跨区完成，释放旧管制区 Zone {currentZoneId} 的锁。");
                }
                
                Console.WriteLine($"MockRobot {Id}: 已到达节点 {CurrentNode}。");
                
                SetState(RobotState.IDLE);
            }
            else
            {
                // 【修复：幽灵死锁漏洞】
                // 车辆在跨区移动途中发生错误（ERROR）或被强行取消时，由于它可能已抢先占用了 targetZoneId 的锁，
                // 如果不释放，将会导致目标区域交通阻断。
                if (currentZoneId != targetZoneId)
                {
                    _trafficController.ReleaseLock(targetZoneId, this.Id);
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
