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
        private readonly Dictionary<int, string> _lockedZones = new Dictionary<int, string>();
        private readonly object _lockObj = new object();
        private readonly int _timeoutMs;

        public TrafficController(int timeoutMs = 10000)
        {
            _timeoutMs = timeoutMs;
        }

        public async Task WaitAndAcquireLockAsync(int zoneId, string robotId)
        {
            int retryCount = 0;
            int maxRetries = _timeoutMs / 100;
            
            while (true)
            {
                lock (_lockObj)
                {
                    if (!_lockedZones.ContainsKey(zoneId) || _lockedZones[zoneId] == robotId)
                    {
                        _lockedZones[zoneId] = robotId;
                        return; // 成功拿到锁
                    }
                }
                
                // 等待排队
                await Task.Delay(100);
                retryCount++;

                if (retryCount > maxRetries)
                {
                    throw new ZoneLockTimeoutException($"Zone Deadlock: {robotId} waiting for Zone {zoneId} has timed out.");
                }
            }
        }

        public void ReleaseLock(int zoneId, string robotId)
        {
            lock (_lockObj)
            {
                if (_lockedZones.ContainsKey(zoneId) && _lockedZones[zoneId] == robotId)
                {
                    _lockedZones.Remove(zoneId);
                }
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
