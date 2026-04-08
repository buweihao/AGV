using System.Threading.Tasks;

namespace BasicRegionNavigation.Core.Interfaces
{
    public interface ITrafficController
    {
        Task WaitAndAcquireLockAsync(string zoneName, string robotId, double priority = 0);
        void ForceAcquireLock(string zoneName, string robotId);
        void ReleaseLock(string zoneName, string robotId);
        void ReleaseAllLocksForRobot(string robotId, string keepZoneName = null);
        void ClearAllLocks();
        System.Collections.Generic.IReadOnlyDictionary<string, string> GetAllLocks();
    }
}
