using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BasicRegionNavigation.Core.Interfaces;

namespace BasicRegionNavigation.Applications.Controllers
{
    public class ZoneLockTimeoutException : Exception
    {
        public ZoneLockTimeoutException(string message) : base(message) { }
    }

    public class TrafficController : ITrafficController
    {
        private readonly Dictionary<string, string> _lockedZones = new Dictionary<string, string>();
        private readonly Dictionary<string, List<(string RobotId, double Priority)>> _waitingQueues = new Dictionary<string, List<(string, double)>>();
        private readonly object _lockObj = new object();
        private readonly int _timeoutMs;

        public TrafficController(int timeoutMs = 60000)
        {
            _timeoutMs = timeoutMs;
        }

        public async Task WaitAndAcquireLockAsync(string zoneName, string robotId, double priority = 0)
        {
            // 【新增修复：重入锁直接放行】
            lock (_lockObj)
            {
                if (_lockedZones.ContainsKey(zoneName) && _lockedZones[zoneName] == robotId)
                {
                    return; // 自己已经拥有这个锁，直接放行，无需重新排队
                }
            }
            int retryCount = 0;
            int maxRetries = _timeoutMs / 100;
            
            lock (_lockObj)
            {
                if (!_waitingQueues.ContainsKey(zoneName))
                {
                    _waitingQueues[zoneName] = new List<(string, double)>();
                }
                if (!_waitingQueues[zoneName].Exists(x => x.RobotId == robotId))
                {
                    _waitingQueues[zoneName].Add((robotId, priority));
                }
            }

            try
            {
            
                while (true)
                {
                    lock (_lockObj)
                    {
                        if (!_waitingQueues.ContainsKey(zoneName) || !_waitingQueues[zoneName].Exists(x => x.RobotId == robotId))
                        {
                            throw new TaskCanceledException($"Lock acquisition canceled for {robotId} on {zoneName}");
                        }

                        // 优先级排序：值越小，排越前面
                        _waitingQueues[zoneName].Sort((a, b) => a.Priority.CompareTo(b.Priority));
                        
                        bool isFirst = _waitingQueues[zoneName].Count > 0 && _waitingQueues[zoneName][0].RobotId == robotId;

                        if (isFirst)
                        {
                            if (!_lockedZones.ContainsKey(zoneName) || _lockedZones[zoneName] == robotId)
                            {
                                _lockedZones[zoneName] = robotId;
                                _waitingQueues[zoneName].RemoveAll(x => x.RobotId == robotId);
                                // 【新增日志】
                                Serilog.Log.Information($"[交通管制] 成功获取锁: 区域={zoneName}, 车辆={robotId}");
                                return;
                            }
                            else 
                            {
                                // 【新增日志】排在第一位但锁被别人拿着
                                Serilog.Log.Warning($"[交通管制] {robotId} 位于队列首位，但区域 {zoneName} 仍被 {_lockedZones[zoneName]} 占用");
                            }
                        }
                        else 
                        {
                            // 【新增周期性日志】每隔 5 秒打印一次排队详情，防止死锁时日志静默
                            if (retryCount % 50 == 0) 
                            {
                                var queueInfo = string.Join(" -> ", System.Linq.Enumerable.Select(_waitingQueues[zoneName], x => $"{x.RobotId}(P:{x.Priority})"));
                                Serilog.Log.Debug($"[交通管制] {robotId} 等待中: 区域={zoneName}, 队列位置={_waitingQueues[zoneName].FindIndex(x => x.RobotId == robotId)}, 完整队列=[{queueInfo}]");
                            }
                        }
                    }
                    
                    // 等待排队
                    await Task.Delay(100);
                    retryCount++;

                    if (retryCount > maxRetries)
                    {
                        throw new ZoneLockTimeoutException($"Zone Deadlock: {robotId} waiting for Zone {zoneName} has timed out.");
                    }
                }
            }
            finally
            {
                lock (_lockObj)
                {
                    if (_waitingQueues.ContainsKey(zoneName))
                    {
                        _waitingQueues[zoneName].RemoveAll(x => x.RobotId == robotId);
                    }
                }
            }
        }

        public void ReleaseLock(string zoneName, string robotId)
        {
            lock (_lockObj)
            {
                if (_lockedZones.ContainsKey(zoneName) && _lockedZones[zoneName] == robotId)
                {
                    _lockedZones.Remove(zoneName);
                }
            }
        }

        public void ForceAcquireLock(string zoneName, string robotId)
        {
            lock (_lockObj)
            {
                _lockedZones[zoneName] = robotId;
            }
        }

        public void ClearAllLocks()
        {
            lock (_lockObj)
            {
                _lockedZones.Clear();
                _waitingQueues.Clear(); // 清理等待队列，触发现有 WaitAndAcquireLockAsync 抛出取消异常
            }
        }

        public IReadOnlyDictionary<string, string> GetAllLocks()
        {
            lock (_lockObj)
            {
                return new Dictionary<string, string>(_lockedZones);
            }
        }
    }
}
