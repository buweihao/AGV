using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Core.Interfaces;
using MyDatabase;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BasicRegionNavigation.Services
{
    public class DatabaseService : IDatabaseService
    {
        private readonly IRepository<TaskHistory> _taskHistoryRepo;
        private readonly IRepository<SystemAlarm> _systemAlarmRepo;

        public DatabaseService(
            IRepository<TaskHistory> taskHistoryRepo,
            IRepository<SystemAlarm> systemAlarmRepo)
        {
            _taskHistoryRepo = taskHistoryRepo;
            _systemAlarmRepo = systemAlarmRepo;
        }

        public async Task InsertTaskHistoryAsync(TaskHistory history)
        {
            await _taskHistoryRepo.InsertAsync(history);
        }

        public async Task UpdateTaskHistoryAsync(TaskHistory history)
        {
            await _taskHistoryRepo.UpdateAsync(history);
        }

        public async Task<List<TaskHistory>> GetTaskHistoriesAsync()
        {
            // 获取所有记录，并将 IEnumerable 显式转换为 List
            var result = await _taskHistoryRepo.GetListAsync(x => true);
            return result.ToList();
        }

        public async Task InsertSystemAlarmAsync(SystemAlarm alarm)
        {
            await _systemAlarmRepo.InsertAsync(alarm);
        }

        public async Task<List<SystemAlarm>> GetSystemAlarmsAsync()
        {
            // 获取所有记录，并将 IEnumerable 显式转换为 List
            var result = await _systemAlarmRepo.GetListAsync(x => true);
            return result.ToList();
        }
    }
}
