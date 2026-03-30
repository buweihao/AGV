using BasicRegionNavigation.Controls;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Helper;
using BasicRegionNavigation.Models;
using BasicRegionNavigation.Services;
using BasicRegionNavigation.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel; // 核心引用
using CommunityToolkit.Mvvm.Input;        // 核心引用
using Core;
using HandyControl.Controls;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using MyConfig.Controls;
using MyDatabase;
using Prism.Events; // 假设 IEventAggregator 来自 Prism
using SkiaSharp;
using SqlSugar;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Expression = System.Linq.Expressions.Expression;
using TaskStatus = BasicRegionNavigation.Core.Entities.TaskStatus;
using Timer = System.Timers.Timer;
using BasicRegionNavigation.Common;
using System.IO;

namespace BasicRegionNavigation.ViewModels
{
    // 修改 1: partial + ObservableObject
    public partial class ViewAViewModel : ObservableObject
    {
        private List<BasicRegionNavigation.Core.Interfaces.IRobot> _robots;
        private BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher _taskDispatcher;
        private ITrafficController _trafficController;

        [ObservableProperty]
        private int _currentNode;

        [ObservableProperty]
        private LogicNode _targetNode;

        [ObservableProperty]
        private double _robot1X;

        [ObservableProperty]
        private double _robot1Y;

        [ObservableProperty]
        private string _robot1StateText = "状态: IDLE";

        [ObservableProperty]
        private double _robot2X;

        [ObservableProperty]
        private double _robot2Y;

        [ObservableProperty]
        private double _robot3X;

        [ObservableProperty]
        private double _robot3Y;

        [ObservableProperty]
        private string _robot3StateText = "状态: IDLE";

        [ObservableProperty]
        private string _robotErrorText;

        [ObservableProperty]
        private Visibility _robotErrorVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private string _newRobotNodeId = "1";

        [ObservableProperty]
        private ObservableCollection<LogicNode> _mapNodes;

        [ObservableProperty]
        private ObservableCollection<MapEdgeViewModel> _mapEdges = new ObservableCollection<MapEdgeViewModel>();

        [ObservableProperty]
        private ObservableCollection<TaskOrder> _activeTasks = new ObservableCollection<TaskOrder>();

        [ObservableProperty]
        private ObservableCollection<IRobot> _robotList;

        [ObservableProperty]
        private ObservableCollection<LockInfo> _activeLocks = new ObservableCollection<LockInfo>();

        private readonly IAlarmHistoryService _alarmHistoryService;
        private readonly IModbusService _modbusService;

        /// <summary>
        /// 本页面专用，表示用户选中需要查看的模组
        /// </summary>
        [ObservableProperty]
        private string _moduleNum = "0";

        /// <summary>
        /// 用于给前端绑定的各个控件的数据源，会跟随模组index变化而更新
        /// </summary>
        [ObservableProperty]
        private ModuleModel _currentModule;

        [ObservableProperty] private string _model1Name = "供料机A(-)";
        [ObservableProperty] private string _model2Name = "供料机B(-)";


        // 模组缓存
        private readonly ConcurrentDictionary<string, ModuleModel> _modulesCache = new ConcurrentDictionary<string, ModuleModel>();

        // 报警信息翻译字典 (Key: UI标识, Value: 中文描述)
        private readonly Dictionary<string, string> _alarmDescriptions = new Dictionary<string, string>
        {
            // 供料机 A
            { "FeederASensorFault",       "供料机A-传感器故障" },
            { "FeederAComponentFault",    "供料机A-气缸/元件故障" },
            { "FeederATraceCommFault",    "供料机A-轨道通讯故障" },
            { "FeederAMasterCommFault",   "供料机A-主控通讯故障" },
            
            // 供料机 B
            { "FeederBSensorFault",       "供料机B-传感器故障" },
            { "FeederBComponentFault",    "供料机B-气缸/元件故障" },
            { "FeederBTraceCommFault",    "供料机B-轨道通讯故障" },
            { "FeederBMasterCommFault",   "供料机B-主控通讯故障" },
            
            // 翻转台
            { "FlipperSensorFault",       "翻转台-传感器故障" },
            { "FlipperComponentFault",    "翻转台-气缸/元件故障" },
            { "FlipperTraceCommFault",    "翻转台-轨道通讯故障" },
            { "FlipperHostCommFault",     "翻转台-上位机通讯故障" },
            { "FlipperRobotCommFault",    "翻转台-机器人通讯故障" },
            { "FlipperDoorTriggered",     "翻转台-安全门触发" },
            { "FlipperSafetyCurtain",     "翻转台-光幕触发" },
            { "FlipperEmergencyStop",     "翻转台-急停按下" },
            { "FlipperScannerCommFault",  "翻转台-扫码枪通讯故障" }
        };

        [ObservableProperty] private ObservableCollection<TaskTemplate> _taskTemplates;
        [ObservableProperty] private TaskTemplate _selectedTaskTemplate;

        [ObservableProperty] private int _autoTaskInterval = 15000;
        [ObservableProperty] private bool _isAutoTaskRunning;
        private CancellationTokenSource _autoTaskCts;

        [ObservableProperty] private double _robotGlobalSpeed = 5.0;
        partial void OnRobotGlobalSpeedChanged(double value) => BasicRegionNavigation.Infrastructure.Robots.MockRobot.GlobalSpeed = value;

        [ObservableProperty] private int _robotChargeTime = 5000;
        partial void OnRobotChargeTimeChanged(int value) => BasicRegionNavigation.Infrastructure.Robots.MockRobot.GlobalChargeTimeMs = value;

        private readonly IProductionService _productionService;
        private readonly ILoggerService _loggerService;
        private readonly IDatabaseService _databaseService; // 【新增】注入数据库服务
        public ViewAViewModel(IModbusService modbusService, IProductionService productionService, IAlarmHistoryService alarmHistoryService, ILoggerService loggerService, IDatabaseService databaseService)
        {
            _loggerService = loggerService;
            _databaseService = databaseService;
            _modbusService = modbusService;
            _alarmHistoryService = alarmHistoryService;
            _productionService = productionService; // 【新增】赋值


            // 1. 加载系统配置 (合并版 JSON)
            LoadSystemConfig();

            TargetNode = MapNodes.FirstOrDefault();

            _trafficController = new BasicRegionNavigation.Applications.Controllers.TrafficController();

            _robots = new List<BasicRegionNavigation.Core.Interfaces.IRobot>();
            RobotList = new ObservableCollection<IRobot>(_robots);

            // 实例化 Dispatcher
            _taskDispatcher = new BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher(_robots, MapNodes, _databaseService, _trafficController);
            _taskDispatcher.OnTaskCompleted += (order) =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1000); // 留出1秒展示"已完成"
                    ActiveTasks.Remove(order);
                });
            };

            InitializeModules(new[] { "1", "2" });
            _modbusService.OnModuleDataChanged += HandleDataChanged;
            InitializeSubscriptions(_modbusService);

            // 启动频率为 500ms 的锁状态轮询
            StartLockMonitoring();
        }

        private void StartLockMonitoring()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    if (_trafficController != null)
                    {
                        var locks = _trafficController.GetAllLocks();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // 增量更新或全量覆盖
                            // 这里简单起见直接全量覆盖
                            ActiveLocks.Clear();
                            foreach (var kvp in locks)
                            {
                                ActiveLocks.Add(new LockInfo { ZoneName = kvp.Key, RobotId = kvp.Value });
                            }
                        });
                    }
                    await Task.Delay(500);
                }
            });
        }

        /// <summary>
        /// 从外部 JSON 加载完整系统配置 (地图 + 任务模版)
        /// </summary>
        private void LoadSystemConfig()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "agv_config.json");
                if (!File.Exists(configPath))
                {
                    configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Configs", "agv_config.json");
                }

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true, // 忽略属性大小写
                        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } // 允许将 JSON 字符串("Normal")转为 Enum
                    };

                    var config = JsonSerializer.Deserialize<AgvSystemConfig>(json, options);

                    if (config != null)
                    {
                        MapNodes = config.MapNodes;
                        TaskTemplates = config.TaskTemplates;
                        SelectedTaskTemplate = TaskTemplates.FirstOrDefault();

                        ComputeDisplayLabels();
                        GenerateEdges(MapNodes);
                        Serilog.Log.Information($"[系统配置] 加载成功. 节点:{MapNodes.Count}, 模板:{TaskTemplates.Count}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error($"[系统配置] 加载异常: {ex.Message}");
            }

            // 兜底逻辑
            MapNodes = new ObservableCollection<LogicNode>
            {
                new LogicNode { Id = 1, X = 100, Y = 100, ConnectedNodeIds = new List<int>{ 2 } },
                new LogicNode { Id = 2, X = 300, Y = 100, ConnectedNodeIds = new List<int>{ 1 } }
            };
            TaskTemplates = new ObservableCollection<TaskTemplate>();
            ComputeDisplayLabels();
            GenerateEdges(MapNodes);
        }

        private void ComputeDisplayLabels()
        {
            if (MapNodes == null) return;
            var groups = MapNodes.GroupBy(n => Global.GetZoneName(n))
                                 .ToDictionary(g => g.Key, g => g.Count());
            
            foreach (var node in MapNodes)
            {
                string zone = Global.GetZoneName(node);
                if (groups.ContainsKey(zone) && groups[zone] >= 2)
                {
                    node.DisplayLabel = zone;
                }
                else
                {
                    node.DisplayLabel = "";
                }
            }
        }

        /// <summary>
        /// 根据节点连接关系动态生成 UI 连线集合
        /// </summary>
        private void GenerateEdges(IEnumerable<LogicNode> nodes)
        {
            var edges = new List<MapEdgeViewModel>();
            var processed = new HashSet<string>();

            foreach (var nodeA in nodes)
            {
                foreach (var nextId in nodeA.ConnectedNodeIds)
                {
                    var nodeB = nodes.FirstOrDefault(n => n.Id == nextId);
                    if (nodeB == null) continue;

                    string edgeKey = nodeA.Id < nodeB.Id ? $"{nodeA.Id}-{nodeB.Id}" : $"{nodeB.Id}-{nodeA.Id}";
                    bool bToA = nodeB.ConnectedNodeIds.Contains(nodeA.Id);

                    if (bToA)
                    {
                        if (!processed.Contains(edgeKey))
                        {
                            edges.Add(new MapEdgeViewModel(nodeA.X, nodeA.Y, nodeB.X, nodeB.Y, false));
                            processed.Add(edgeKey);
                        }
                    }
                    else
                    {
                        edges.Add(new MapEdgeViewModel(nodeA.X, nodeA.Y, nodeB.X, nodeB.Y, true));
                    }
                }
            }
            MapEdges = new ObservableCollection<MapEdgeViewModel>(edges);
        }

        // 在 MainViewModel 或初始化逻辑中
        public void InitializeSubscriptions(IModbusService modbusService)
        {
            // 定义需要订阅的模组 ID 列表
            var moduleIds = new[] { "1", "2" };

            foreach (var moduleId in moduleIds)
            {
                // --- A. 订阅状态 (Status) ---
                var statusMapping = new Dictionary<string, string>
                {
                    // 1. 周边墩子 (Peripheral) - 名字要和 CSV 里的 TagName 对应
                    { "FeedStation1Status",        "PLC_Peripheral_FeedStation1Status" },
                    { "FeedStation2Status",        "PLC_Peripheral_FeedStation2Status" },
                    { "FeedStation3Status",        "PLC_Peripheral_FeedStation3Status" },
                    { "HangerOkStation1Status",    "PLC_Peripheral_HangerOkStation1Status" }, // 注意 Station1
                    { "HangerOkStation2Status",    "PLC_Peripheral_HangerOkStation2Status" }, // 注意 Station2
                    { "HangerNgStationStatus",     "PLC_Peripheral_HangerNgStationStatus" },
                
                    // 2. 机械手 (Robot)
                    { "ProductRobotStatus",        "PLC_Robot_ProductStatus" },
                    { "HangerRobotStatus",         "PLC_Robot_HangerStatus" },
                
                    // 3. 供料机 (Feeder) - 注意这里做了“改名”映射
                    // CSV里叫 PLC_Feeder_A_Status，UI里叫 FeederAStatus，Mapping 负责桥接
                    { "FeederAStatus",             "PLC_Feeder_A_Status" },
                    { "FeederBStatus",             "PLC_Feeder_B_Status" },
                
                    // 4. 翻转台 (Flipper)
                    { "FlipperStatus",             "PLC_Flipper_Status" }
                };

                modbusService.SubscribeDynamicGroup(
                    moduleId: moduleId,
                    category: ModuleDataCategory.Status,
                    fieldMapping: statusMapping
                );

                // --- B. 订阅产能 (Capacity) ---
                var capacityMapping = new Dictionary<string, string>
                {
                    { "FeederACapacity", "PLC_Feeder_A_TotalCapacity" },
                    { "FeederBCapacity", "PLC_Feeder_B_TotalCapacity" },
                    // 翻转台产能
                    { "FlipperCapacity", "PLC_Flipper_TotalCapacity" }
                };

                modbusService.SubscribeDynamicGroup(
                    moduleId: moduleId,
                    category: ModuleDataCategory.Capacity,
                    fieldMapping: capacityMapping
                );

                // --- C. 订阅 24小时产能点位 ---
                var hourlyCapacityMapping = new Dictionary<string, string>();
                for (int i = 0; i < 12; i++) hourlyCapacityMapping.Add($"Day_{i}", $"PLC_Flipper_Hourly_CapacityDay{i}");
                for (int i = 0; i < 12; i++) hourlyCapacityMapping.Add($"Night_{i}", $"PLC_Flipper_Hourly_CapacityNight{i}");

                modbusService.SubscribeDynamicGroup(
                    moduleId: moduleId,
                    category: ModuleDataCategory.UpColumnSeries,
                    fieldMapping: hourlyCapacityMapping
                );

                // --- D. 订阅产品信息 ---
                var productInfoMapping = new Dictionary<string, string>
        {
            { "ProjectCode", "PLC_Flipper_ProjectNo" },
            { "Material",    "PLC_Flipper_ProductType" },
            { "AnodeType",   "PLC_Flipper_AnodeType" },
            { "Color",       "PLC_Flipper_ProductColor" }
        };
                modbusService.SubscribeDynamicGroup(moduleId: moduleId, category: ModuleDataCategory.UpProductInfo, fieldMapping: productInfoMapping);
                modbusService.SubscribeDynamicGroup(moduleId: moduleId, category: ModuleDataCategory.DnProductInfo, fieldMapping: productInfoMapping);

                // --- E. 报警信息订阅 ---
                var warningMapping = new Dictionary<string, string>
        {
            { "FeederASensorFault",       "PLC_Feeder_A_SensorFault" },
            { "FeederAComponentFault",    "PLC_Feeder_A_ComponentFault" },
            { "FeederATraceCommFault",    "PLC_Feeder_A_TraceCommFault" },
            { "FeederAMasterCommFault",   "PLC_Feeder_A_MasterCommFault" },
            { "FeederBSensorFault",       "PLC_Feeder_B_SensorFault" },
            { "FeederBComponentFault",    "PLC_Feeder_B_ComponentFault" },
            { "FeederBTraceCommFault",    "PLC_Feeder_B_TraceCommFault" },
            { "FeederBMasterCommFault",   "PLC_Feeder_B_MasterCommFault" },
            { "FlipperSensorFault",       "PLC_Flipper_SensorFault" },
            { "FlipperComponentFault",    "PLC_Flipper_ComponentFault" },
            { "FlipperTraceCommFault",    "PLC_Flipper_TraceCommFault" },
            { "FlipperHostCommFault",     "PLC_Flipper_HostCommFault" },
            { "FlipperRobotCommFault",    "PLC_Flipper_RobotCommFault" },
            { "FlipperDoorTriggered",     "PLC_Flipper_DoorTriggered" },
            { "FlipperSafetyCurtain",     "PLC_Flipper_SafetyCurtainTriggered" },
            { "FlipperEmergencyStop",     "PLC_Flipper_EmergencyStop" },
            { "FlipperScannerCommFault",  "PLC_Flipper_ScannerCommFault" }
        };

                modbusService.SubscribeDynamicGroup(
                    moduleId: moduleId,
                    category: ModuleDataCategory.WarningInfo,
                    fieldMapping: warningMapping
                );

                // =========================================================
                // [新增] F. 订阅供料机项目号 (用于动态显示名称)
                // =========================================================
                // 注意：这里使用了你提供的 CSV 中的 TagName
                var feederProjectMapping = new Dictionary<string, string>
                {
                    { "FeederA_Project", "PLC_Feeder_A_ProjectNo" },
                    { "FeederB_Project", "PLC_Feeder_B_ProjectNo" }
                };

                // 我们复用 UpProductInfo 类别，或者你可以去 Enum 定义一个新的
                // 这里为了方便演示，假设我们定义了一个新的类别或者复用现有的
                // 建议在 ModuleDataCategory 枚举中新增一项：FeederProjectInfo
                modbusService.SubscribeDynamicGroup(
                    moduleId: moduleId,
                    category: ModuleDataCategory.FeederProjectInfo, // 需在枚举中定义
                    fieldMapping: feederProjectMapping
                );


            }
        }
        private void InitializeModules(string[] ids)
        {
            foreach (var id in ids)
            {
                var model = new ModuleModel(id);
                _modulesCache.TryAdd(id, model);
            }

            // 默认显示第一个
            if (ids.Length > 0) CurrentModule = _modulesCache[ids[0]];
        }

        // 3. 交通指挥：收到数据 -> 查找字典 -> 定点更新
        // [修改] 数据处理中心 (兼容 Dictionary<string, object>)
        // [修改 3] 数据处理中心
        // [修改 3] 数据处理中心
        // [修改] 数据处理中心
        private void HandleDataChanged(string moduleId, ModuleDataCategory category, object data)
        {
            // =========================================================
            // 1. 处理柱状图数据 (UpColumnSeries / DnColumnSeries)
            // =========================================================
            if (category == ModuleDataCategory.UpColumnSeries || category == ModuleDataCategory.DnColumnSeries)
            {
                if (data is System.Collections.IDictionary dict)
                {
                    var processedDict = new Dictionary<string, double>();
                    bool isDayNightData = false;

                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        string key = entry.Key?.ToString();
                        if (string.IsNullOrEmpty(key)) continue;

                        double val = 0;
                        try { val = Convert.ToDouble(entry.Value); } catch { }
                        processedDict[key] = val;

                        // 判断是否为 24 小时点位 (Day_x 或 Night_x)
                        if (key.StartsWith("Day_") || key.StartsWith("Night_")) isDayNightData = true;
                    }

                    double[] finalArray;
                    if (isDayNightData)
                    {
                        // 获取当前实际班次
                        var currentClass = Global.GetCurrentClassTime();
                        bool isDayShift = currentClass.Status == ClassStatus.白班;
                        string prefix = isDayShift ? "Day_" : "Night_";

                        finalArray = new double[12];
                        for (int i = 0; i < 12; i++)
                        {
                            string key = $"{prefix}{i}";
                            finalArray[i] = processedDict.ContainsKey(key) ? processedDict[key] : 0;
                        }

                        // 【核心修改】：通过 moduleId 找到对应的模组对象并更新其 X 轴标签
                        if (_modulesCache.TryGetValue(moduleId, out var targetModuleForLabels))
                        {
                            Application.Current.Dispatcher.Invoke(() => UpdateXLabelsByTime(targetModuleForLabels));
                        }
                    }
                    else
                    {
                        // 普通索引数据处理 (0, 1, 2...)
                        int maxIndex = 0;
                        foreach (var key in processedDict.Keys)
                            if (int.TryParse(key, out int idx) && idx > maxIndex) maxIndex = idx;

                        finalArray = new double[maxIndex + 1];
                        foreach (var kvp in processedDict)
                            if (int.TryParse(kvp.Key, out int idx)) finalArray[idx] = kvp.Value;
                    }
                    data = finalArray;
                }

                // 将处理好的数组分发给指定模组
                if (_modulesCache.TryGetValue(moduleId, out var targetModule))
                {
                    targetModule.DispatchData(category, data);
                }
                return;
            }

            // =========================================================
            // 2. 处理产品信息 (UpProductInfo / DnProductInfo)
            // =========================================================
            else if (category == ModuleDataCategory.UpProductInfo || category == ModuleDataCategory.DnProductInfo)
            {
                if (data is System.Collections.IDictionary dict)
                {
                    var stringDict = new Dictionary<string, string>();
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        string key = entry.Key?.ToString();
                        if (!string.IsNullOrEmpty(key)) stringDict[key] = entry.Value?.ToString() ?? "";
                    }
                    data = stringDict;
                }
            }

            // =========================================================
            // 3. 处理报警信息 (WarningInfo)
            // =========================================================
            else if (category == ModuleDataCategory.WarningInfo)
            {
                if (data is System.Collections.IDictionary dict)
                {
                    var activeAlarms = new List<AlarmInfo>();
                    int indexCounter = 1;

                    // 这里需要有一个能把 moduleId 转换成 "模组1" 这种友好文本的逻辑，
                    // 假设您的 moduleId 就是 "1" 或者您可以根据业务替换
                    string moduleName = $"模组{moduleId}";

                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        string key = entry.Key?.ToString();
                        if (string.IsNullOrEmpty(key)) continue;

                        bool isTriggered = false;
                        if (entry.Value is bool bVal) isTriggered = bVal;
                        else if (entry.Value is int iVal) isTriggered = iVal != 0;
                        else if (entry.Value is string sVal) isTriggered = (sVal == "1" || sVal.Equals("True", StringComparison.OrdinalIgnoreCase));

                        // ==========================================================
                        // 无论报警与否，先解析出正确的设备名称和报警内容
                        // ==========================================================
                        string fullMsg = _alarmDescriptions.ContainsKey(key) ? _alarmDescriptions[key] : $"未知设备-未知报警: {key}";
                        string originalDeviceName = "未知设备";
                        string descText = fullMsg;

                        var parts = fullMsg.Split(new[] { '-', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            originalDeviceName = parts[0];
                            descText = parts[1];
                        }

                        // 拼接模组名称，结果例如："模组1 翻转台"
                        string finalDeviceName = $"{moduleName} {originalDeviceName}";

                        // ==========================================================
                        // 【核心】在这里调用状态追踪逻辑！
                        // 为了防止不同模组有相同的报警 Key 发生冲突，用 moduleId + key 作为唯一标识
                        // ==========================================================
                        string uniqueAlarmKey = $"{moduleId}_{key}";
                        _alarmHistoryService.ProcessAlarmSignalAsync(uniqueAlarmKey, isTriggered, finalDeviceName, descText);

                        // ==========================================================
                        // 下面的逻辑保持不变，用于更新实时 UI 界面
                        // ==========================================================
                        if (isTriggered)
                        {
                            activeAlarms.Add(new AlarmInfo
                            {
                                Index = indexCounter++,
                                PropertyKey = key,
                                Time = DateTime.Now,
                                Device = finalDeviceName, // 这里也可以直接用拼接好的名字显示在实时列表
                                Description = descText
                            });
                        }
                    }

                    // 更新指定模组的报警列表 (UI 更新逻辑不变)
                    if (_modulesCache.TryGetValue(moduleId, out var module))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var collection = module.CurrentWarningInfo?.AlarmList;
                            if (collection != null)
                            {
                                collection.Clear();
                                foreach (var alarm in activeAlarms) collection.Add(alarm);
                            }
                        });
                        return;
                    }
                }
            }

            // =========================================================
            // [新增] 5. 处理供料机项目号并格式化字符串
            // =========================================================
            else if (category == ModuleDataCategory.FeederProjectInfo)
            {
                if (data is System.Collections.IDictionary dict)
                {
                    // 1. 提取数据
                    string projA = dict.Contains("FeederA_Project") ? dict["FeederA_Project"]?.ToString() : null;
                    string projB = dict.Contains("FeederB_Project") ? dict["FeederB_Project"]?.ToString() : null;

                    // 2. 更新到缓存 (ModuleModel)，以便切换 Tab 时能恢复显示
                    if (_modulesCache.TryGetValue(moduleId, out var module))
                    {
                        if (projA != null) module.CacheFeederAProject = projA;
                        if (projB != null) module.CacheFeederBProject = projB;
                    }

                    // 3. 只有当数据属于“当前选中的模组”时，才更新 UI 属性
                    // 这里的 CurrentModule.ModuleId 需要根据你 ModuleModel 的实际定义来获取
                    if (CurrentModule != null && CurrentModule.Id == moduleId)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (projA != null) Model1Name = $"供料机A({projA})";
                            if (projB != null) Model2Name = $"供料机B({projB})";
                        });
                    }
                }
                return;
            }

            // =========================================================
            // 4. 分发其他类型的数据 (Status, Capacity 等)
            // =========================================================
            if (_modulesCache.TryGetValue(moduleId, out var genericTarget))
            {
                genericTarget.DispatchData(category, data);
            }
        }

        // 切换模组的方法 (供前端 ComboBox 绑定)
        public void SwitchModule(string newId)
        {
            if (_modulesCache.TryGetValue(newId, out var model))
            {
                CurrentModule = model;

                // 切换时，从缓存中读取该模组上一次记录的项目号，刷新 UI
                // 如果缓存为空，则显示默认值
                string projA = !string.IsNullOrEmpty(model.CacheFeederAProject) ? model.CacheFeederAProject : "-";
                string projB = !string.IsNullOrEmpty(model.CacheFeederBProject) ? model.CacheFeederBProject : "-";

                Model1Name = $"供料机A({projA})";
                Model2Name = $"供料机B({projB})";
            }
        }

        [RelayCommand]
        private async Task NavigateModule(string index)
        {
            SwitchModule(index);
        }

        [ObservableProperty]
        private ObservableCollection<ISeries> _revenueSeries;

        [ObservableProperty]
        private int[] _myIntDataArray = new int[] { 10, 50, 25, 60, 90 };

        // 修改：增加 ModuleModel 参数
        public void UpdateXLabelsByTime(ModuleModel module)
        {
            if (module?.CurrentColumnInfo?.XAxes == null || module.CurrentColumnInfo.XAxes.Length == 0)
                return;

            string[] labels;
            var currentClassTime = Global.GetCurrentClassTime();

            if (currentClassTime.Status == ClassStatus.白班)
                labels = new[] { "8", "9", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19" };
            else
                labels = new[] { "20", "21", "22", "23", "0", "1", "2", "3", "4", "5", "6", "7" };

            // 更新传入模组的标签，而不是全局的 CurrentModule
            module.CurrentColumnInfo.XAxes[0].Labels = labels;
        }


        [RelayCommand]
        private void ResetRobot()
        {
            foreach (var r in _robots) (r as BasicRegionNavigation.Infrastructure.Robots.MockRobot)?.Reset();
            RobotErrorVisibility = Visibility.Collapsed;
        }

        [RelayCommand]
        private void DispatchSelectedTemplate()
        {
            if (SelectedTaskTemplate == null) return;

            var order = new TaskOrder
            {
                OrderId = "CFG-" + Guid.NewGuid().ToString().Substring(0, 4),
                Status = TaskStatus.Waiting
            };

            foreach (var stage in SelectedTaskTemplate.Stages)
            {
                order.Stages.Enqueue(new TaskStage
                {
                    TargetNodeId = stage.TargetNodeId,
                    WaitTimeMs = stage.WaitTimeMs,
                    StageName = stage.StageName,
                    ActionCode = stage.ActionCode,
                    DynamicTargetType = stage.DynamicTargetType,
                    CandidateNodeIds = stage.CandidateNodeIds != null ? new List<int>(stage.CandidateNodeIds) : new List<int>()
                });
            }

            ActiveTasks.Add(order);
            _taskDispatcher.SubmitTask(order);

            Serilog.Log.Information($"[配置派单] 已根据模板 {SelectedTaskTemplate.TemplateName} 下发任务 {order.OrderId}");
        }

        [RelayCommand]
        private void ClearRobots()
        {
            foreach (var r in _robots)
            {
                var mr = r as BasicRegionNavigation.Infrastructure.Robots.MockRobot;
                mr?.Cancel();
            }

            _trafficController?.ClearAllLocks();

            _robots.Clear();
            RobotList.Clear();
            ActiveTasks.Clear();

            _taskDispatcher?.UnsubscribeFromRobots();
        }

        [RelayCommand]
        private void AddRobot()
        {
            if (!int.TryParse(NewRobotNodeId, out int nodeId)) return;
            var node = MapNodes.FirstOrDefault(n => n.Id == nodeId);
            if (node == null)
            {
                RobotErrorText = $"找不到节点 {nodeId}";
                RobotErrorVisibility = Visibility.Visible;
                return;
            }

            var trafficCtrl = _trafficController;
            string newId = $"AGV-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";

            var robot = new BasicRegionNavigation.Infrastructure.Robots.MockRobot(
                id: newId,
                trafficController: trafficCtrl,
                logger: _loggerService,
                mapNodes: MapNodes,
                onError: (errorMsg) => { Application.Current.Dispatcher.Invoke(() => { RobotErrorText = $"{newId}: {errorMsg}"; RobotErrorVisibility = Visibility.Visible; }); }
            );

            robot.CurrentNode = node.Id;
            robot.CurrentX = node.X;
            robot.CurrentY = node.Y;

            _robots.Add(robot);
            RobotList.Add(robot);

            trafficCtrl.ForceAcquireLock(Global.GetZoneName(node), newId);

            _taskDispatcher?.UnsubscribeFromRobots();
            
            _taskDispatcher = new BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher(_robots, MapNodes, _databaseService, trafficCtrl);
            _taskDispatcher.OnTaskCompleted += (order) =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1000);
                    ActiveTasks.Remove(order);
                });
            };
        }
        [RelayCommand]
        private void ToggleAutoTaskGenerator()
        {
            if (IsAutoTaskRunning)
            {
                _autoTaskCts?.Cancel();
                IsAutoTaskRunning = false;
                Serilog.Log.Information("[自动化任务] 已停止生成器");
            }
            else
            {
                _autoTaskCts = new CancellationTokenSource();
                IsAutoTaskRunning = true;
                _ = RunAutoTaskGeneratorAsync(_autoTaskCts.Token);
                Serilog.Log.Information($"[自动化任务] 已启动生成器 (间隔: {AutoTaskInterval}ms)");
            }
        }

        private async Task RunAutoTaskGeneratorAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (TaskTemplates != null && TaskTemplates.Any())
                    {
                        var template = TaskTemplates[Random.Shared.Next(TaskTemplates.Count)];
                        
                        var order = new TaskOrder
                        {
                            OrderId = "AUTO-" + Guid.NewGuid().ToString().Substring(0, 4).ToUpper(),
                            Status = TaskStatus.Waiting
                        };

                        foreach (var stage in template.Stages)
                        {
                            order.Stages.Enqueue(new TaskStage
                            {
                                TargetNodeId = stage.TargetNodeId,
                                WaitTimeMs = stage.WaitTimeMs,
                                StageName = stage.StageName,
                                ActionCode = stage.ActionCode,
                                DynamicTargetType = stage.DynamicTargetType,
                                CandidateNodeIds = stage.CandidateNodeIds != null ? new List<int>(stage.CandidateNodeIds) : new List<int>()
                            });
                        }

                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            ActiveTasks.Add(order);
                            _taskDispatcher.SubmitTask(order);
                        });

                        Serilog.Log.Information($"[自动化任务] 已自动下发任务 {order.OrderId} (基于模版: {template.TemplateName})");
                    }
                    
                    await Task.Delay(AutoTaskInterval, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Serilog.Log.Error($"[自动化任务] 指令生成循环发生故障: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() => IsAutoTaskRunning = false);
            }
        }
    }
}
