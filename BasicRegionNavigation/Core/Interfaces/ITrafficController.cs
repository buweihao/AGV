using System.Threading.Tasks;

namespace BasicRegionNavigation.Core.Interfaces
{
    public interface ITrafficController
    {
        Task WaitAndAcquireLockAsync(int zoneId, string robotId);
        void ReleaseLock(int zoneId, string robotId);
        void ClearAllLocks();
    }
}
