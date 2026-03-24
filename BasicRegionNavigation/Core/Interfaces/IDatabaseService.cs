using BasicRegionNavigation.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BasicRegionNavigation.Core.Interfaces
{
    public interface IDatabaseService
    {
        /// <summary>
        /// 插入任务历史记录
        /// </summary>
        Task InsertTaskHistoryAsync(TaskHistory history);

        /// <summary>
        /// 更新任务历史记录 (完成/异常时回写状态和结束时间)
        /// </summary>
        Task UpdateTaskHistoryAsync(TaskHistory history);

        /// <summary>
        /// 获取所有任务历史记录
        /// </summary>
        Task<List<TaskHistory>> GetTaskHistoriesAsync();

        /// <summary>
        /// 插入系统报警记录
        /// </summary>
        Task InsertSystemAlarmAsync(SystemAlarm alarm);

        /// <summary>
        /// 获取所有系统报警记录
        /// </summary>
        Task<List<SystemAlarm>> GetSystemAlarmsAsync();
    }
}
