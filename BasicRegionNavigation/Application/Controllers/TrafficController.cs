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
                        // 优先级排序：值越小，排越前面
                        _waitingQueues[zoneName].Sort((a, b) => a.Priority.CompareTo(b.Priority));
                        
                        bool isFirst = _waitingQueues[zoneName].Count > 0 && _waitingQueues[zoneName][0].RobotId == robotId;

                        if (isFirst)
                        {
                            if (!_lockedZones.ContainsKey(zoneName) || _lockedZones[zoneName] == robotId)
                            {
                                _lockedZones[zoneName] = robotId;
                                _waitingQueues[zoneName].RemoveAll(x => x.RobotId == robotId);
                                return; // 成功拿到锁
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
