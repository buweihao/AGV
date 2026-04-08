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
                    // 【需求1：锁申请记录】
                    var heldByRequester = string.Join(",", System.Linq.Enumerable.Select(
                        System.Linq.Enumerable.Where(_lockedZones, kv => kv.Value == robotId), kv => kv.Key));
                    Serilog.Log.Debug($"[交通管制] 车辆 {robotId} 发起申请 -> 区域 {zoneName} (当前持有锁: [{(string.IsNullOrEmpty(heldByRequester) ? "无" : heldByRequester)}], 优先级={priority:F1})");
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
                                // 【需求3：强化死锁警告，附双方持仓快照】
                                string blockerRobotId = _lockedZones[zoneName];
                                var requesterHolding = string.Join(",", System.Linq.Enumerable.Select(
                                    System.Linq.Enumerable.Where(_lockedZones, kv => kv.Value == robotId), kv => kv.Key));
                                var blockerHolding = string.Join(",", System.Linq.Enumerable.Select(
                                    System.Linq.Enumerable.Where(_lockedZones, kv => kv.Value == blockerRobotId), kv => kv.Key));
                                var requesterQueuing = string.Join(",", System.Linq.Enumerable.Select(
                                    System.Linq.Enumerable.Where(_waitingQueues, kvq => System.Linq.Enumerable.Any(kvq.Value, x => x.RobotId == robotId)), kvq => kvq.Key));
                                var blockerQueuing = string.Join(",", System.Linq.Enumerable.Select(
                                    System.Linq.Enumerable.Where(_waitingQueues, kvq => System.Linq.Enumerable.Any(kvq.Value, x => x.RobotId == blockerRobotId)), kvq => kvq.Key));
                                Serilog.Log.Warning(
                                    $"[交通管制] 死锁警告: {robotId}(当前持有:[{(string.IsNullOrEmpty(requesterHolding) ? "无" : requesterHolding)}], 排队中:[{(string.IsNullOrEmpty(requesterQueuing) ? "无" : requesterQueuing)}]) " +
                                    $"正在排队等待 {zoneName}，但该区域被 {blockerRobotId}(当前持有:[{(string.IsNullOrEmpty(blockerHolding) ? "无" : blockerHolding)}], 排队中:[{(string.IsNullOrEmpty(blockerQueuing) ? "无" : blockerQueuing)}]) 占用。");
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
                        // 【新增修复：死锁全局快照】
                        lock (_lockObj)
                        {
                            var lockStatus = string.Join(", ", System.Linq.Enumerable.Select(_lockedZones, kv => $"{kv.Key}:{kv.Value}"));
                            var queueStatus = _waitingQueues.ContainsKey(zoneName) 
                                ? string.Join(" <- ", System.Linq.Enumerable.Select(_waitingQueues[zoneName], x => $"{x.RobotId}"))
                                : "Empty";
                            Serilog.Log.Error($"[死锁爆炸] {robotId} 等待 {zoneName} 锁超时！当前全网锁状态: [{lockStatus}], 当前区域等待队列: [{queueStatus}]");
                        }
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
                    Serilog.Log.Information($"[交通管制] 释放锁: 区域={zoneName}, 车辆={robotId}");
                }
                else
                {
                    // 【需求4：释放失败校验】
                    if (!_lockedZones.ContainsKey(zoneName))
                    {
                        Serilog.Log.Warning($"[交通管制] ⚠️ 释放锁失败: 车辆 {robotId} 尝试释放区域 [{zoneName}]，但该区域锁根本不存在（可能已被兜底清理或从未成功申请）。");
                    }
                    else
                    {
                        string actualHolder = _lockedZones[zoneName];
                        Serilog.Log.Warning($"[交通管制] ⚠️ 释放锁失败: 车辆 {robotId} 尝试释放区域 [{zoneName}]，但该锁目前由 [{actualHolder}] 持有，拒绝越权释放（可能为幽灵调用）。");
                    }
                }
            }
        }

        public void ReleaseAllLocksForRobot(string robotId, string keepZoneName = null)
        {
            lock (_lockObj)
            {
                // 找到该车辆持有的所有锁
                var zonesHeldByRobot = new List<string>();
                foreach (var kvp in _lockedZones)
                {
                    if (kvp.Value == robotId)
                    {
                        zonesHeldByRobot.Add(kvp.Key);
                    }
                }

                // 移除除了当前物理区域之外的所有遗留锁
                foreach (var zone in zonesHeldByRobot)
                {
                    if (zone != keepZoneName)
                    {
                        _lockedZones.Remove(zone);
                        Serilog.Log.Information($"[交通管制] 兜底清理: 释放了车辆 {robotId} 遗留的幽灵锁 [{zone}]");
                    }
                }

                // 同时清理该车辆可能遗留在等待队列中的幽灵排队
                foreach (var queue in _waitingQueues.Values)
                {
                    queue.RemoveAll(x => x.RobotId == robotId);
                }
            }
        }

        public void ForceAcquireLock(string zoneName, string robotId)
        {
            lock (_lockObj)
            {
                _lockedZones[zoneName] = robotId;
                // 【新增日志】强制上锁
                Serilog.Log.Warning($"[交通管制] 强制上锁: 区域={zoneName}, 车辆={robotId}");
            }
        }

        public void ClearAllLocks()
        {
            lock (_lockObj)
            {
                _lockedZones.Clear();
                _waitingQueues.Clear(); // 清理等待队列，触发现有 WaitAndAcquireLockAsync 抛出取消异常
                // 【新增日志】
                Serilog.Log.Warning("[交通管制] 触发全网交通锁清空！已强制驱散所有等待队列及占用锁。");
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
