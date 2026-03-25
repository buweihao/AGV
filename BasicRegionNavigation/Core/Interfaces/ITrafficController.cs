using System.Threading.Tasks;

namespace BasicRegionNavigation.Core.Interfaces
{
    public interface ITrafficController
    {
        Task WaitAndAcquireLockAsync(string zoneName, string robotId);
        void ForceAcquireLock(string zoneName, string robotId);
        void ReleaseLock(string zoneName, string robotId);
        void ClearAllLocks();
    }
}
