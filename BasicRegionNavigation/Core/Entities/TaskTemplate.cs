using System.Collections.Generic;
using System.Collections.ObjectModel;
using BasicRegionNavigation.Core.Entities;

namespace BasicRegionNavigation.Core.Entities
{
    /// <summary>
    /// 标准化的任务模板：定义一组可重复调度的任务流
    /// </summary>
    public class TaskTemplate
    {
        public string TemplateId { get; set; }
        public string TemplateName { get; set; }
        public List<TaskStage> Stages { get; set; }
    }

    /// <summary>
    /// 系统全局配置对象 (合并地图点位与任务模版)
    /// </summary>
    public class AgvSystemConfig
    {
        public ObservableCollection<LogicNode> MapNodes { get; set; }
        public ObservableCollection<TaskTemplate> TaskTemplates { get; set; }
    }
}
