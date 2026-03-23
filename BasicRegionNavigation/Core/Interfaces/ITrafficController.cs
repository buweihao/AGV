using System.Threading.Tasks;

namespace BasicRegionNavigation.Core.Interfaces
{
    public interface ITrafficController
    {
        Task WaitAndAcquireLockAsync(int nodeId, string robotId);
        void ReleaseLock(int nodeId, string robotId);
    }
}
