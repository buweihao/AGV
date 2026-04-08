using BasicRegionNavigation.Common;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Core.Interfaces;
using BasicRegionNavigation.Services;
using Core;
using System;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BasicRegionNavigation.Infrastructure.Robots
{
    public partial class MockRobot : ObservableObject, IRobot
    {
        public static double GlobalSpeed { get; set; } = 5.0; // 【新增】全局移动步长控制
        public static int GlobalChargeTimeMs { get; set; } = 5000; // 【新增】全局充电耗率控制

        [ObservableProperty] private string _id;
        [ObservableProperty] private int _currentNode;
        [ObservableProperty] private double _currentX;
        [ObservableProperty] private double _currentY;
        [ObservableProperty] private RobotState _state = RobotState.IDLE;
        [ObservableProperty] private double _batteryLevel = 100;
        [ObservableProperty] private string _currentTaskDesc = "-";

        // --- 【新增统计指标】 ---
        [ObservableProperty] private double _distancePerMinute;
        [ObservableProperty] private double _tasksPerMinute;
        [ObservableProperty] private double _batteryPerMinute;
        [ObservableProperty] private double _maxStopSec;
        [ObservableProperty] private string _maxStopReasonText = "无";

        private double _totalDistance;
        private int _totalTasks;
        private double _totalBatteryUsed;
        private double _tempDistance; // 暂存本阶段距离
        private DateTime _lastTaskEndTime;
        
        // 用于滑动窗口统计 (最近 60s)
        private readonly System.Collections.Concurrent.ConcurrentQueue<(DateTime Time, double Value)> _distanceHistory = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(DateTime Time, int Value)> _tasksHistory = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(DateTime Time, double Value)> _batteryHistory = new();

        private DateTime? _stopStartTime;
        private string _currentStopReason;

        public string CurrentStateText => State.ToString();

        partial void OnStateChanged(RobotState value) => OnPropertyChanged(nameof(CurrentStateText));

        public event Action<double, double> OnPositionChanged;
        public event Action<int> OnNodeChanged;
        public event Action<RobotState> OnRobotStateChanged; // 改个名字避免与局部 Partial 方法冲突
        public event Action<IRobot> OnBatteryLow;

        private bool _isCharging = false;

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
            
            // 启动后台统计刷新任务 (每 2s 聚合一次)
            _ = Task.Run(RefreshStatsLoop);
        }

        private async Task RefreshStatsLoop()
        {
            while (true)
            {
                try
                {
                    var now = DateTime.Now;
                    var cutoff = now.AddMinutes(-1);

                    // 1. 清理过期数据并求和
                    DistancePerMinute = CalculateRollingSum(_distanceHistory, cutoff);
                    TasksPerMinute = CalculateRollingSum(_tasksHistory, cutoff);
                    BatteryPerMinute = CalculateRollingSum(_batteryHistory, cutoff);

                    // 2. 如果当前处于停止状态（且非人为 IDLE），累加当前停止时长
                    if (_stopStartTime.HasValue && State != RobotState.MOVING && State != RobotState.CHARGING)
                    {
                        var currentStopSec = (now - _stopStartTime.Value).TotalSeconds;
                        if (currentStopSec > MaxStopSec)
                        {
                            MaxStopSec = currentStopSec;
                            MaxStopReasonText = _currentStopReason ?? "交通拥堵";
                        }
                    }
                }
                catch { }
                await Task.Delay(2000);
            }
        }

        private double CalculateRollingSum(System.Collections.Concurrent.ConcurrentQueue<(DateTime Time, double Value)> history, DateTime cutoff)
        {
            while (history.TryPeek(out var item) && item.Time < cutoff) history.TryDequeue(out _);
            double sum = 0;
            foreach (var item in history) sum += item.Value;
            return sum;
        }

        private double CalculateRollingSum(System.Collections.Concurrent.ConcurrentQueue<(DateTime Time, int Value)> history, DateTime cutoff)
        {
            while (history.TryPeek(out var item) && item.Time < cutoff) history.TryDequeue(out _);
            double sum = 0;
            foreach (var item in history) sum += item.Value;
            return sum;
        }

        public void RecordStop(string reason)
        {
            if (!_stopStartTime.HasValue)
            {
                _stopStartTime = DateTime.Now;
                _currentStopReason = reason;
            }
        }

        private void ClearStop()
        {
            _stopStartTime = null;
            _currentStopReason = null;
        }

        private void SetState(RobotState state)
        {
            if (state == RobotState.MOVING || state == RobotState.CHARGING)
            {
                ClearStop(); // 开始移动或充电，清除停止计时
            }
            else if (State == RobotState.MOVING && state == RobotState.IDLE)
            {
                // 如果是移动完成变为空闲，不计入 Stoppage (常规状态)
                _stopStartTime = null; 
            }
            else if (!_stopStartTime.HasValue)
            {
                // 进入 IDLE/PAUSED/WAIT 状态，记录开始时间
                _stopStartTime = DateTime.Now;
                _currentStopReason = "空闲等待";
            }

            State = state;
            _onStateUpdate?.Invoke(state);
            OnRobotStateChanged?.Invoke(state); // 改个名字
        }

        public async Task GoToNodeAsync(LogicNode ultimateTargetNode)
        {
            CurrentTaskDesc = $"前往节点 {ultimateTargetNode.Id}";
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

            try
            {
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
                        try
                        {
                            await _trafficController.WaitAndAcquireLockAsync(nextZone, this.Id);
                        }
                        catch (BasicRegionNavigation.Applications.Controllers.ZoneLockTimeoutException)
                        {
                            blockedNodes.Add(nextNode.Id);
                            needsReroute = true;
                            break;
                        }
                    }

                    // 物理移动：按实际物理距离计算出行驶时间，然后线性插值平滑动画
                    {
                        double startX = CurrentX;
                        double startY = CurrentY;

                        // 优先从当前节点的字典取配置的实际物理距离；若未配置则降级用坐标距离
                        double actualEdgeLength = currNodeObj.GetActualDistance(nextNode.Id);
                        if (actualEdgeLength <= 0)
                        {
                            double dx0 = nextNode.X - startX;
                            double dy0 = nextNode.Y - startY;
                            actualEdgeLength = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
                        }

                        // 总行驶时间（秒）= 物理距离 / 速度；速度单位与距离单位相同
                        // GlobalSpeed 的含义沿用历史 px/tick，这里我们以"每tick = 50ms"换算为 m/s
                        // 即 speedMs = GlobalSpeed 米/秒（可在UI调节）
                        double speedMeterPerSec = GlobalSpeed;
                        double travelSeconds = actualEdgeLength / speedMeterPerSec;
                        int totalSteps = Math.Max(1, (int)(travelSeconds * 1000.0 / 50.0)); // 每步 50ms

                        for (int step = 1; step <= totalSteps; step++)
                        {
                            if (_cancelFlag) break;
                            double ratio = (double)step / totalSteps;
                            CurrentX = startX + (nextNode.X - startX) * ratio;
                            CurrentY = startY + (nextNode.Y - startY) * ratio;
                            OnPositionChanged?.Invoke(CurrentX, CurrentY);
                            
                            // 距离统计
                            _distanceHistory.Enqueue((DateTime.Now, actualEdgeLength / totalSteps));

                            await Task.Delay(50);
                        }

                        // 确保精确到达目标坐标
                        if (!_cancelFlag)
                        {
                            CurrentX = nextNode.X;
                            CurrentY = nextNode.Y;
                            OnPositionChanged?.Invoke(CurrentX, CurrentY);
                        }
                    }

                    if (!_cancelFlag)
                    {
                        // 到达节点：更新状态与电量
                        CurrentNode = nextNode.Id;
                        OnNodeChanged?.Invoke(CurrentNode);

                        // 每次移动到一个节点，消耗 2% 电量
                        double consumed = 2.0;
                        BatteryLevel = Math.Max(0, BatteryLevel - consumed);
                        _batteryHistory.Enqueue((DateTime.Now, consumed));

                        if (BatteryLevel <= 20 && !_isCharging)
                        {
                            OnBatteryLow?.Invoke(this);
                        }

                        // 后松旧藤蔓
                        if (currentZone != nextZone)
                        {
                            _trafficController.ReleaseLock(currentZone, this.Id);
                        }
                        currNodeObj = nextNode;
                    }
                    else
                    {
                        // 取消时安全释放已申请但未进入的锁
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
                // 到达终点后的特殊逻辑：充电处理
                if (ultimateTargetNode.NodeType == NodeType.Charging)
                {
                    _isCharging = true;
                    SetState(RobotState.CHARGING);
                    Serilog.Log.Debug($"{Id }到达充电位置，开始快速补电...");
                    await Task.Delay(GlobalChargeTimeMs); // 使用全局充电时间
                    BatteryLevel = 100;
                    _isCharging = false;
                    Serilog.Log.Debug($"{Id}补电完成，电量 -> 100%。");

                }

                SetState(RobotState.IDLE);
                CurrentTaskDesc = "-";
                
                // 任务完成统计 (1 min 滑动窗口)
                _tasksHistory.Enqueue((DateTime.Now, 1));

                Serilog.Log.Debug($"MockRobot {Id}: 任务已完成。");
            }
                else if (_cancelFlag)
                {
                    throw new TaskCanceledException($"MockRobot {Id} move task was canceled.");
                }
            }
            finally
            {
                if (_trafficController != null && _mapNodes != null)
                {
                    var currentNodeObj = System.Linq.Enumerable.FirstOrDefault(_mapNodes, n => n.Id == CurrentNode);
                    string keepZone = currentNodeObj != null ? Global.GetZoneName(currentNodeObj) : null;
                    _trafficController.ReleaseAllLocksForRobot(this.Id, keepZone);
                    Serilog.Log.Information($"MockRobot {Id}: GoToNodeAsync 退出(完成/异常/取消)，执行锁清理兜底。物理保留区域: {keepZone ?? "无"}");
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
