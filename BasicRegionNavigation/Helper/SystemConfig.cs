using System.Linq; // 必须引用 Linq

namespace BasicRegionNavigation.Helper
{
    public static class SystemConfig
    {
        // ==========================================
        // 1. 基础常量定义
        // ==========================================
        public const int ModuleCount = 2;
        public static readonly string[] Modules = new[] { "1","2" };
        public const string TriggerSuffix = "ReadTrigger"; // 统一后缀

        // ==========================================
        // 2. 设备名称常量池 (字典/常量库)
        // ==========================================
        public const string Dev_UpFeeder_A = "PLC_Feeder_A";
        public const string Dev_UpFeeder_B = "PLC_Feeder_B";
        public const string Dev_DownFeeder_A = "PLC_UnFeeder_A";
        public const string Dev_DownFeeder_B = "PLC_UnFeeder_B";
        public const string Dev_UpFlipper = "PLC_Flipper";
        public const string Dev_Peripheral = "PLC_Peripheral";
        public const string Dev_Robot = "PLC_Robot";

        // ==========================================
        // 3. 业务启用列表 (在这里控制项目模式)
        // ==========================================

        // 【场景 A：上料 + 翻转】
        public static readonly string[] ActiveUpLoaders = new[] { Dev_UpFeeder_A, Dev_UpFeeder_B };
        public static readonly string[] ActiveDownLoaders = new string[] { };
        public static readonly string[] FlipperDevice = new[] { Dev_UpFlipper };

        // 【场景 B：纯下料 + 翻转】(切换时只需解注这里)
        //public static readonly string[] ActiveUpLoaders = new string[] { };
        //public static readonly string[] ActiveDownLoaders = new[] { Dev_DownFeeder_A, Dev_DownFeeder_B };
        //public static readonly string[] FlipperDevice = new[] { Dev_UpFlipper };

        // ==========================================
        // 4. 自动计算的集合 (不要手动写，自动生成)
        // ==========================================

        /// <summary>
        /// 所有启用的供料机 (上料 + 下料)
        /// 用于：小时产能采集、转产信号监听
        /// </summary>
        public static readonly string[] AllActiveFeeders = ActiveUpLoaders
                                                           .Concat(ActiveDownLoaders)
                                                           .ToArray();

        /// <summary>
        /// 所有需要时间同步的设备 (上料 + 下料 + 翻转)
        /// 用于：时间同步任务
        /// </summary>
        public static readonly string[] AllTimeSyncDevices = ActiveUpLoaders
                                                             .Concat(ActiveDownLoaders)
                                                             .Concat(FlipperDevice)
                                                             .ToArray();

        /// <summary>
        /// 定义具体的元组类型 (Template, ModuleId, Ip) 组成的数组
        /// </summary>
        public static readonly (string Template, string ModuleId, string Ip)[] CloneList = new[]
        {
    // 测试 (保留原样)
    //(Template: Dev_Peripheral,     ModuleId: "1", Ip: "127.0.0.1"),
    //(Template: Dev_Robot,          ModuleId: "1", Ip: "127.0.0.1"),
    //(Template: Dev_UpFeeder_A,   ModuleId: "1", Ip: "127.0.0.2"),
    //(Template: Dev_UpFeeder_B,   ModuleId: "1", Ip: "127.0.0.3"),
    //(Template: Dev_UpFlipper,        ModuleId: "1", Ip: "127.0.0.1"),

    //(Template: Dev_Peripheral,     ModuleId: "2", Ip: "127.0.0.4"),
    //(Template: Dev_Robot,          ModuleId: "2", Ip: "127.0.0.4"),
    //(Template: Dev_UpFeeder_A,   ModuleId: "2", Ip: "127.0.0.5"),
    //(Template: Dev_UpFeeder_B,   ModuleId: "2", Ip: "127.0.0.6"),
    //(Template: Dev_UpFlipper,        ModuleId: "2", Ip: "127.0.0.4"),

    //// 3楼
    // 模组1
    (Template: Dev_Peripheral,     ModuleId: "1", Ip: "10.120.93.82"), // 翻转台
    (Template: Dev_Robot,          ModuleId: "1", Ip: "10.120.93.82"), // 大机械手
    (Template: Dev_UpFeeder_A,   ModuleId: "1", Ip: "10.120.93.80"), // 供料机A
    (Template: Dev_UpFeeder_B,   ModuleId: "1", Ip: "10.120.93.81"), // 供料机B
    (Template: Dev_UpFlipper,        ModuleId: "1", Ip: "10.120.93.82"), // 翻转台
    // 模组2
    (Template: Dev_Peripheral,     ModuleId: "2", Ip: "10.120.93.89"), // 翻转台
    (Template: Dev_Robot,          ModuleId: "2", Ip: "10.120.93.89"), // 大机械手
    (Template: Dev_UpFeeder_A,   ModuleId: "2", Ip: "10.120.93.87"), // 供料机A
    (Template: Dev_UpFeeder_B,   ModuleId: "2", Ip: "10.120.93.88"), // 供料机B
    (Template: Dev_UpFlipper,        ModuleId: "2", Ip: "10.120.93.89"), // 翻转台

    //// 5楼
    //// 模组1
    //(Template: Dev_Peripheral,     ModuleId: "1", Ip: "10.120.93.99"),  // 翻转台
    //(Template: Dev_Robot,          ModuleId: "1", Ip: "10.120.93.102"), // 大机械手
    //(Template: Dev_UpFeeder_A,   ModuleId: "1", Ip: "10.120.93.97"),  // 供料机A
    //(Template: Dev_UpFeeder_B,   ModuleId: "1", Ip: "10.120.93.98"),  // 供料机B
    //(Template: Dev_UpFlipper,        ModuleId: "1", Ip: "10.120.93.99"),  // 翻转台
    //// 模组2
    //(Template: Dev_Peripheral,     ModuleId: "2", Ip: "10.120.93.106"), // 翻转台
    //(Template: Dev_Robot,          ModuleId: "2", Ip: "10.120.93.109"), // 大机械手
    //(Template: Dev_UpFeeder_A,   ModuleId: "2", Ip: "10.120.93.104"), // 供料机A
    //(Template: Dev_UpFeeder_B,   ModuleId: "2", Ip: "10.120.93.105"), // 供料机B
    //(Template: Dev_UpFlipper,        ModuleId: "2", Ip: "10.120.93.106"), // 翻转台

    //// 6楼
    //// 模组1
    //(Template: Dev_Peripheral,     ModuleId: "1", Ip: "10.120.93.116"), // 翻转台
    //(Template: Dev_Robot,          ModuleId: "1", Ip: "10.120.93.119"), // 大机械手
    //(Template: Dev_UpFeeder_A,   ModuleId: "1", Ip: "10.120.93.114"), // 下料机A
    //(Template: Dev_UpFeeder_B,   ModuleId: "1", Ip: "10.120.93.115"), // 下料机B
    //(Template: Dev_UpFlipper,        ModuleId: "1", Ip: "10.120.93.116"), // 翻转台
    //// 模组2
    //(Template: Dev_Peripheral,     ModuleId: "2", Ip: "10.120.93.123"), // 翻转台
    //(Template: Dev_Robot,          ModuleId: "2", Ip: "10.120.93.126"), // 大机械手
    //(Template: Dev_UpFeeder_A,   ModuleId: "2", Ip: "10.120.93.121"), // 下料机A
    //(Template: Dev_UpFeeder_B,   ModuleId: "2", Ip: "10.120.93.122"), // 下料机B
    //(Template: Dev_UpFlipper,        ModuleId: "2", Ip: "10.120.93.123"), // 翻转台
};
        public static readonly Dictionary<string, string> ValueTranslationMap = new(StringComparer.OrdinalIgnoreCase)
{


    // ---新增：图片中的颜色映射---
    { "CARBON", "黑色" },
    { "PURE SILVER", "银色" },
    { "PURESILVER", "银色" },
    { "NICKEL", "镍色" },
    { "COPPER", "铜色" },
    { "IRIS", "兰色" },      // 对应图片中的"兰色"
    { "MOSS", "苔藓色" },
    { "FUSCHIA", "梅红色" }  // 对应图片拼写
};
        public static readonly Dictionary<string, string> DeviceNameTranslationMap = new()
        {
            { Dev_UpFeeder_A,   "供料机A" },
            { Dev_UpFeeder_B,   "供料机B" },
            { Dev_DownFeeder_A, "下料机A" },
            { Dev_DownFeeder_B, "下料机B" },
            { Dev_UpFlipper,    "翻转台" },
            { Dev_Peripheral,   "辅助设备" }, // 或 "流道"
            { Dev_Robot,        "机械手" }
        };
        /// 将技术名称转换为中文名称
        /// 例如: "1_PLC_Feeder_A" -> "模组1 供料机A"
        /// </summary>
        public static string GetFriendlyDeviceName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return rawName;

            // 假设命名格式为: "{ModuleId}_{TemplateName}" (例如: 1_PLC_Feeder_A)
            // 我们寻找第一个下划线的位置来分割
            int firstUnderscoreIndex = rawName.IndexOf('_');

            if (firstUnderscoreIndex > 0)
            {
                string moduleId = rawName.Substring(0, firstUnderscoreIndex);
                string templateName = rawName.Substring(firstUnderscoreIndex + 1);

                // 尝试查找中文翻译
                if (DeviceNameTranslationMap.TryGetValue(templateName, out string cnName))
                {
                    return $"模组{moduleId} {cnName}";
                }
            }

            // 如果不符合格式或找不到翻译，返回原值
            return rawName;
        }
    }
}