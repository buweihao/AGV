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
        private readonly System.Collections.Generic.IEnumerable<LogicNode> _mapNodes;

        public MockRobot(string id, ITrafficController trafficController, System.Collections.Generic.IEnumerable<LogicNode> mapNodes = null, Action<RobotState> onStateUpdate = null, Action<string> onError = null)
        {
            Id = id;
            _trafficController = trafficController;
            _mapNodes = mapNodes;
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

        public async Task GoToNodeAsync(LogicNode ultimateTargetNode)
        {
            if (State == RobotState.ERROR)
            {
                Console.WriteLine($"MockRobot {Id}: 无法执行任务，当前状态为 ERROR。");
                return;
            }

            if (State != RobotState.IDLE)
            {
                Console.WriteLine($"MockRobot {Id}: 正在执行任务中 ({State})，忽略本次指令。");
                return;
            }

            SetState(RobotState.MOVING);
            _cancelFlag = false;

            // 如果没有传入地图源，直接报错
            if (_mapNodes == null)
            {
                SetState(RobotState.ERROR);
                _onError?.Invoke($"引擎错误：MockRobot {Id} 未获知全局地图 _mapNodes ！");
                return;
            }

            // 1. 获取寻路队列
            var startNode = System.Linq.Enumerable.FirstOrDefault(_mapNodes, n => n.Id == CurrentNode);
            if (startNode == null)
            {
                SetState(RobotState.ERROR);
                _onError?.Invoke($"引擎错误：起点 {CurrentNode} 不存在于路网中！");
                return;
            }

            // A* 获取真实有序路径（不含起点本身）
            var path = PathFinder.FindPath(startNode, ultimateTargetNode, _mapNodes);
            if (path.Count == 0 && startNode.Id != ultimateTargetNode.Id)
            {
                SetState(RobotState.ERROR);
                _onError?.Invoke($"寻路失败：无法从 {startNode.Id} 到达目标 {ultimateTargetNode.Id}！");
                return;
            }

            Console.WriteLine($"MockRobot {Id}: A*算法已生成路线 -> [{string.Join(" -> ", System.Linq.Enumerable.Select(path, p => p.Id))}]");

            // 2. 步进滚动式交管保护 (Rolling Lock)
            foreach (var nextNode in path)
            {
                if (_cancelFlag) break;

                // 计算是否发生跨区边界
                int currentZoneId = Global.GetZoneId(CurrentNode);
                int targetZoneId = Global.GetZoneId(nextNode.Id);

                if (currentZoneId != targetZoneId)
                {
                    Console.WriteLine($"MockRobot {Id}: 准备跨出管制边界，正在申请前方管制区 Zone {targetZoneId} 的锁...");
                    try
                    {
                        // 细粒度的前进锁：必须先抢下前方的交通空域权，才能松开后方的旧锁！
                        await _trafficController.WaitAndAcquireLockAsync(targetZoneId, this.Id);
                    }
                    catch (BasicRegionNavigation.Applications.Controllers.ZoneLockTimeoutException ex)
                    {
                        Console.WriteLine($"MockRobot {Id}: 发生系统级管制区超时死锁异常 - {ex.Message}");
                        _cancelFlag = true;
                        SetState(RobotState.ERROR);
                        _onError?.Invoke("通信或物理占区10秒无果(发生局部死锁)，已抛出异常并暂停小车");
                        return; // 中断前行，此车因并没有真正跨过去，直接保留 currentZoneId 即合法
                    }
                }
                else
                {
                    // 在同一个防撞区域内 (比如都在一条管线里且路权归自己)
                    // console.log("同区步进无需重复套锁");
                }

                // 准备开始位移 (5 像素步进刷新法)
                Console.WriteLine($"MockRobot {Id}: 离开节点 {CurrentNode} -> 步进驶向 {nextNode.Id}...");

                while (true)
                {
                    if (_cancelFlag) break;

                    double dx = nextNode.X - CurrentX;
                    double dy = nextNode.Y - CurrentY;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    // 到达了该路径锚点
                    if (distance <= 5.0)
                    {
                        CurrentX = nextNode.X;
                        CurrentY = nextNode.Y;
                        break;
                    }

                    // 5像素定长逼近
                    double ratio = 5.0 / distance;
                    CurrentX += dx * ratio;
                    CurrentY += dy * ratio;

                    OnPositionChanged?.Invoke(CurrentX, CurrentY);
                    
                    // 模拟真实的缓慢行驶过程
                    await Task.Delay(50);
                }

                // 一段一程的扫尾
                if (!_cancelFlag)
                {
                    CurrentNode = nextNode.Id;
                    OnNodeChanged?.Invoke(CurrentNode);

                    // 【核心释放】如果刚刚发生了跨区并顺利把腿迈了过来，那现在才是天经地义地把旧路锁让给别人的时候！
                    if (currentZoneId != targetZoneId)
                    {
                        _trafficController.ReleaseLock(currentZoneId, this.Id);
                        Console.WriteLine($"MockRobot {Id}: 旧域腾空 -> 无缝交接！已跨区完成，释放脚后跟的遗留管制区 Zone {currentZoneId}。");
                    }
                }
                else
                {
                    // 如果中途人工取消撤回了（或者是错误了）
                    // 若它正准备跨区但在路上撤消，它永远不能去终点了，就必须把抢跑还没踏进去的 targetZoneId 还回来，它自己待在 currentZone 原地保命就行
                    if (currentZoneId != targetZoneId)
                    {
                        _trafficController.ReleaseLock(targetZoneId, this.Id);
                    }
                    break;
                }
            }

            if (!_cancelFlag)
            {
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
