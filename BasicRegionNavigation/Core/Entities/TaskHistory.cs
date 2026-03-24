using SqlSugar;
using System;

namespace BasicRegionNavigation.Core.Entities
{
    [SugarTable("TaskHistory")]
    public class TaskHistory
    {
        [SugarColumn(IsPrimaryKey = true)]
        public string TaskId { get; set; }

        public string RobotId { get; set; }

        public int StartNode { get; set; }

        public int EndNode { get; set; }

        public DateTime CreateTime { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? FinishTime { get; set; }

        public int Status { get; set; }
    }
}
