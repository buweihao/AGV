using MyDatabase;
using MyLog;
using SqlSugar;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BasicRegionNavigation.Services
{
    // =====================================================================
    // 1. 定义数据库实体模型 (Model)
    // =====================================================================
    [SugarTable("Alarm_History_Records")]
    public class AlarmHistoryRecord
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }

        public string DeviceName { get; set; }       // 设备名 (如: 模组1 翻转台)

        public string AlarmDescription { get; set; } // 报警描述 (如: 急停按下)

        public DateTime StartTime { get; set; }      // 报警触发时间

        [SugarColumn(IsNullable = true)]
        public DateTime? EndTime { get; set; }       // 报警结束时间 (允许为空)

        // 界面显示的计算属性，必须加 IsIgnore，防止 SqlSugar 将其映射到数据库
        [SugarColumn(IsIgnore = true)]
        public string StatusText
        {
            get
            {
                if (!EndTime.HasValue) return "报警中...";
                TimeSpan duration = EndTime.Value - StartTime;
                return $"已解除 (持续 {Math.Round(duration.TotalSeconds)} 秒)";
            }
        }
    }

    // =====================================================================
    // 2. 定义服务接口
    // =====================================================================
    public interface IAlarmHistoryService : IMyLogConfig
    {
        /// <summary>
        /// 处理并追踪报警信号状态，自动写入数据库
        /// </summary>
        Task ProcessAlarmSignalAsync(string uniqueKey, bool isTriggered, string deviceName, string description);

        Task<List<AlarmHistoryRecord>> QueryAlarmsAsync(string deviceKeyword, DateTime startTime, DateTime endTime);
    }

    // =====================================================================
    // 3. 实现服务类
    // =====================================================================
    public class AlarmHistoryService : IAlarmHistoryService
    {
        // 明确注入：报警记录表的仓储
        private readonly IRepository<AlarmHistoryRecord> _alarmRepo;

        // 使用 ConcurrentDictionary 保证多线程读写安全 (状态追踪)
        private readonly ConcurrentDictionary<string, AlarmHistoryRecord> _activeAlarms = new();

        public AlarmHistoryService(IRepository<AlarmHistoryRecord> alarmRepo)
        {
            _alarmRepo = alarmRepo;
        }

        // 配置 Serilog 日志
        public MyLogOptions Configure()
        {
            return new MyLogOptions
            {
                MinimumLevel = Serilog.Events.LogEventLevel.Information,
                EnableConsole = true,
                EnableFile = true,
                FilePath = "logs/AlarmHistoryService.log",
                OutputTemplate = "{Timestamp:HH:mm:ss} [AlarmService] {Message:lj}{NewLine}{Exception}"
            };
        }

        public async Task ProcessAlarmSignalAsync(string uniqueKey, bool isTriggered, string deviceName, string description)
        {
            bool isCurrentlyActive = _activeAlarms.ContainsKey(uniqueKey);

            try
            {
                // 场景 A：触发报警 (False -> True)
                if (isTriggered && !isCurrentlyActive)
                {
                    var newRecord = new AlarmHistoryRecord
                    {
                        DeviceName = deviceName,
                        AlarmDescription = description,
                        StartTime = DateTime.Now,
                        EndTime = null
                    };

                    // 1. 尝试加入内存追踪字典 (并发安全)
                    if (_activeAlarms.TryAdd(uniqueKey, newRecord))
                    {
                        // 2. 执行数据库 Insert 操作
                        // 注意：SqlSugar 默认在 Insert 后会将生成的 Id 回写给 newRecord
                        await _alarmRepo.InsertAsync(newRecord);

                        Console.WriteLine($"[报警触发] 写入数据库: {deviceName} - {description}");
                    }
                }
                // 场景 B：报警解除 (True -> False)
                else if (!isTriggered && isCurrentlyActive)
                {
                    // 1. 尝试从字典中移除并获取该记录
                    if (_activeAlarms.TryRemove(uniqueKey, out var activeRecord))
                    {
                        activeRecord.EndTime = DateTime.Now;

                        // 2. 执行数据库 Update 操作，更新 EndTime
                        await _alarmRepo.UpdateAsync(activeRecord);

                        Console.WriteLine($"[报警解除] 更新数据库: {deviceName} - {description}，持续时间: {activeRecord.StatusText}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[报警处理异常] {deviceName} - {description} : {ex.Message}");
            }
        }

        public async Task<List<AlarmHistoryRecord>> QueryAlarmsAsync(string deviceKeyword, DateTime startTime, DateTime endTime)
        {
            try
            {
                // 建议在设备列表里加一个"全部"选项，如果是"全部"或空，就不去过滤设备名
                bool checkDevice = !string.IsNullOrEmpty(deviceKeyword) && deviceKeyword != "全部";

                // 使用仓储的 GetListAsync 进行组合条件查询
                var list = await _alarmRepo.GetListAsync(x =>
                    x.StartTime >= startTime &&
                    x.StartTime <= endTime &&
                    // 使用 SqlSugar 的模糊匹配，最终会翻译为 SQL 的 LIKE '%关键字%'
                    (!checkDevice || x.DeviceName.Contains(deviceKeyword))
                );

                // 返回按时间倒序排列的数据（最新的报警在最上面）
                return list.OrderByDescending(x => x.StartTime).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[报警查询异常] {ex.Message}");
                return new List<AlarmHistoryRecord>();
            }
        }
    }
}

