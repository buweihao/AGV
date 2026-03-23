using System.Collections.Generic;
using System.Threading.Tasks;
using BasicRegionNavigation.Core.Interfaces;

namespace BasicRegionNavigation.Applications.Controllers
{
    public class TrafficController : ITrafficController
    {
        private readonly Dictionary<int, string> _lockedNodes = new Dictionary<int, string>();
        private readonly object _lockObj = new object();

        public async Task WaitAndAcquireLockAsync(int nodeId, string robotId)
        {
            int retryCount = 0;
            while (true)
            {
                lock (_lockObj)
                {
                    if (!_lockedNodes.ContainsKey(nodeId) || _lockedNodes[nodeId] == robotId)
                    {
                        _lockedNodes[nodeId] = robotId;
                        return; // 成功拿到锁
                    }
                }
                
                // 等待后重新尝试
                await Task.Delay(100);
                retryCount++;

                // 如果等待超过了10秒 (100次*100ms)，证明发生了两车互相等待的死锁！
                // 此时主动抛出异常来打断死锁链，让 MockRobot 进入 ERROR 状态并解开自己的锁。
                if (retryCount > 100)
                {
                    throw new System.TimeoutException($"Deadlock: {robotId} wait for {nodeId} timeout!");
                }
            }
        }

        public void ReleaseLock(int nodeId, string robotId)
        {
            lock (_lockObj)
            {
                if (_lockedNodes.ContainsKey(nodeId) && _lockedNodes[nodeId] == robotId)
                {
                    _lockedNodes.Remove(nodeId);
                }
            }
        }
    }
}
