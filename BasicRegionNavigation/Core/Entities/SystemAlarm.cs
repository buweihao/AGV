using SqlSugar;
using System;

namespace BasicRegionNavigation.Core.Entities
{
    [SugarTable("SystemAlarm")]
    public class SystemAlarm
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string AlarmId { get; set; }

        public int Level { get; set; }

        public string Message { get; set; }

        public DateTime Time { get; set; }
    }
}
