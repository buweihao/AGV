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
        private readonly object _lockObj = new object();
        private readonly int _timeoutMs;

        public TrafficController(int timeoutMs = 10000)
        {
            _timeoutMs = timeoutMs;
        }

        public async Task WaitAndAcquireLockAsync(string zoneName, string robotId)
        {
            int retryCount = 0;
            int maxRetries = _timeoutMs / 100;
            
            while (true)
            {
                lock (_lockObj)
                {
                    if (!_lockedZones.ContainsKey(zoneName) || _lockedZones[zoneName] == robotId)
                    {
                        _lockedZones[zoneName] = robotId;
                        return; // 成功拿到锁
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
    }
}
