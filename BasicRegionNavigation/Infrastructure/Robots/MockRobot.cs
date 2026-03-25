using BasicRegionNavigation.Common;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Core.Interfaces;
using BasicRegionNavigation.Services;
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
        private readonly ILoggerService _logger; // 【新增】日志服务
        private readonly System.Collections.Generic.IEnumerable<LogicNode> _mapNodes;

        public MockRobot(string id, 
                        ITrafficController trafficController, 
                        ILoggerService logger, // 【新增】注入日志服务
                        System.Collections.Generic.IEnumerable<LogicNode> mapNodes = null, 
                        Action<RobotState> onStateUpdate = null, 
                        Action<string> onError = null)
        {
            Id = id;
            _trafficController = trafficController;
            _logger = logger;
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

            // 【方案 B：动态重寻路】初始化黑名单，记录导致死锁/长时间等待的节点
            var blockedNodes = new HashSet<int>();
            bool reachedTarget = false;

            while (!reachedTarget && !_cancelFlag)
            {
                // 1. 获取寻路队列
                var startNode = System.Linq.Enumerable.FirstOrDefault(_mapNodes, n => n.Id == CurrentNode);
                if (startNode == null)
                {
                    SetState(RobotState.ERROR);
                    _onError?.Invoke($"引擎错误：起点 {CurrentNode} 不存在于路网中！");
                    return;
                }

                // 调用增强后的 A*，传入黑名单
                var path = PathFinder.FindPath(startNode, ultimateTargetNode, _mapNodes, blockedNodes);
                if (path.Count == 0)
                {
                    if (startNode.Id == ultimateTargetNode.Id)
                    {
                        reachedTarget = true;
                        continue;
                    }
                    SetState(RobotState.ERROR);
                    _onError?.Invoke($"寻路失败：在黑名单移除 {blockedNodes.Count} 个节点后无路可通！");
                    return;
                }

                Serilog.Log.Debug($"MockRobot {Id}: 寻路策略已更新，生成路线 -> [{string.Join(" -> ", System.Linq.Enumerable.Select(path, p => p.Id))}]");

                LogicNode currNodeObj = startNode;
                bool needsReroute = false;

                // 2. 步进执行 (交替握锁：猴子荡秋千)
                foreach (var nextNode in path)
                {
                    if (_cancelFlag) break;

                    string currentZone = Global.GetZoneName(currNodeObj);
                    string nextZone = Global.GetZoneName(nextNode);

                    // 先抓新藤蔓：如果跨区，必须先申请到前方区域的锁
                    if (currentZone != nextZone)
                    {
                        Console.WriteLine($"MockRobot {Id}: 准备申请前方管制区 {nextZone} 的锁...");
                        try
                        {
                            await _trafficController.WaitAndAcquireLockAsync(nextZone, this.Id);
                        }
                        catch (BasicRegionNavigation.Applications.Controllers.ZoneLockTimeoutException)
                        {
                            Serilog.Log.Warning($"MockRobot {Id}: 前方区域 {nextZone} 超时占用，尝试绕路。将节点 {nextNode.Id} 标记为障碍。");
                            blockedNodes.Add(nextNode.Id);
                            needsReroute = true;
                            break;
                        }
                    }

                    // 物理移动：执行实际的移动延迟
                    while (true)
                    {
                        if (_cancelFlag) break;
                        double dx = nextNode.X - CurrentX;
                        double dy = nextNode.Y - CurrentY;
                        double distance = Math.Sqrt(dx * dx + dy * dy);
                        if (distance <= 5.0) { CurrentX = nextNode.X; CurrentY = nextNode.Y; break; }
                        double ratio = 5.0 / distance;
                        CurrentX += dx * ratio; CurrentY += dy * ratio;
                        OnPositionChanged?.Invoke(CurrentX, CurrentY);
                        await Task.Delay(50);
                    }

                    if (!_cancelFlag)
                    {
                        // 更新位置：移动到位后更新节点
                        CurrentNode = nextNode.Id;
                        OnNodeChanged?.Invoke(CurrentNode);

                        // 后松旧藤蔓：如果刚才发生了跨区，物理车身已离开旧区，释放旧锁
                        if (currentZone != nextZone)
                        {
                            _trafficController.ReleaseLock(currentZone, this.Id);
                        }
                        currNodeObj = nextNode;
                    }
                    else
                    {
                        // 取消时安全处理
                        if (currentZone != nextZone) _trafficController.ReleaseLock(nextZone, this.Id);
                        break;
                    }
                } // 结束 foreach (nextNode in path)

                if (!_cancelFlag && !needsReroute)
                {
                    reachedTarget = true;
                }
            } // 结束 while (!reachedTarget)

            if (!_cancelFlag && reachedTarget)
            {
                SetState(RobotState.IDLE);
                Serilog.Log.Debug($"MockRobot {Id}: 任务全部顺利完成。");
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
