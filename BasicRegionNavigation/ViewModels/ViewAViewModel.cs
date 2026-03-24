using BasicRegionNavigation.Controls;
using BasicRegionNavigation.Core.Entities;
using BasicRegionNavigation.Helper;
using BasicRegionNavigation.Models;
using BasicRegionNavigation.Services;
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
using Timer = System.Timers.Timer;

namespace BasicRegionNavigation.ViewModels
{
    // 修改 1: partial + ObservableObject
    public partial class ViewAViewModel : ObservableObject
    {
        private List<BasicRegionNavigation.Core.Interfaces.IRobot> _robots;
        private BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher _taskDispatcher;

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
        private string _robot2StateText = "状态: IDLE";

        [ObservableProperty]
        private string _robotPositionText = "坐标实时监控已简化";

        [ObservableProperty]
        private string _robotErrorText;

        [ObservableProperty]
        private Visibility _robotErrorVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private ObservableCollection<LogicNode> _mapNodes;

        [ObservableProperty]
        private ObservableCollection<TaskOrder> _activeTasks = new ObservableCollection<TaskOrder>();

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
        private readonly IProductionService _productionService; // 【新增】注入生产服务
        public ViewAViewModel(IModbusService modbusService, IProductionService productionService, IAlarmHistoryService alarmHistoryService)
        {
            _modbusService = modbusService;
            _alarmHistoryService = alarmHistoryService;
            _productionService = productionService; // 【新增】赋值

            // 实例化 MapNodes 和路网（重置为人为设计的十字路口+单行带结构）
            MapNodes = new ObservableCollection<LogicNode>
            {
                // 横向单行带区域：Node 2, 3, 4 (属于 Zone 1)
                new LogicNode { Id = 1, X = 100, Y = 100, ConnectedNodeIds = new List<int> { 2 } }, // Zone 10001 (入口)
                new LogicNode { Id = 2, X = 250, Y = 100, ConnectedNodeIds = new List<int> { 1, 3 } },
                new LogicNode { Id = 3, X = 400, Y = 100, ConnectedNodeIds = new List<int> { 2, 4, 6, 12 } },
                new LogicNode { Id = 4, X = 550, Y = 100, ConnectedNodeIds = new List<int> { 3, 5 } },
                new LogicNode { Id = 5, X = 700, Y = 100, ConnectedNodeIds = new List<int> { 4, 12 } }, // Zone 10005 (出口)
                
                // 纵向及十字路口区域：Node 6, 7, 8 (属于 Zone 2)
                new LogicNode { Id = 6, X = 400, Y = 250, ConnectedNodeIds = new List<int> { 3, 7, 12 } },
                new LogicNode { Id = 7, X = 400, Y = 400, ConnectedNodeIds = new List<int> { 6, 8, 9, 10 } },
                new LogicNode { Id = 8, X = 400, Y = 550, ConnectedNodeIds = new List<int> { 7, 11 } },
                new LogicNode { Id = 11, X = 400, Y = 650, ConnectedNodeIds = new List<int> { 8 } }, // Zone 10011 (向下脱离区)
                
                // 十字路口的横向外延 (安全区)
                new LogicNode { Id = 9, X = 100, Y = 400, ConnectedNodeIds = new List<int> { 7 } },
                new LogicNode { Id = 10, X = 700, Y = 400, ConnectedNodeIds = new List<int> { 7 } },
                
                // 专门用于动态避让的驻车湾 (Buffer Node)
                new LogicNode { Id = 12, X = 470, Y = 160, IsBufferNode = true, ConnectedNodeIds = new List<int> { 3, 5, 6 } } 
            };

            TargetNode = MapNodes.FirstOrDefault();

            var trafficController = new BasicRegionNavigation.Applications.Controllers.TrafficController();

            // 实例化两台小车，并注入路网字典供 A* 寻路参考
            var robot1 = new BasicRegionNavigation.Infrastructure.Robots.MockRobot(
                id: "AGV-1",
                trafficController: trafficController,
                mapNodes: MapNodes,
                onStateUpdate: (state) => { Application.Current.Dispatcher.Invoke(() => { Robot1StateText = $"状态: {state}"; }); },
                onError: (errorMsg) => { Application.Current.Dispatcher.Invoke(() => { RobotErrorText = $"AGV-1: {errorMsg}"; RobotErrorVisibility = Visibility.Visible; }); }
            );
            robot1.CurrentNode = 1;
            robot1.CurrentX = 100;
            robot1.CurrentY = 100;
            Robot1X = 100; Robot1Y = 100;
            robot1.OnPositionChanged += (x, y) => { Application.Current.Dispatcher.Invoke(() => { Robot1X = x; Robot1Y = y; }); };

            var robot2 = new BasicRegionNavigation.Infrastructure.Robots.MockRobot(
                id: "AGV-2",
                trafficController: trafficController,
                mapNodes: MapNodes,
                onStateUpdate: (state) => { Application.Current.Dispatcher.Invoke(() => { Robot2StateText = $"状态: {state}"; }); },
                onError: (errorMsg) => { Application.Current.Dispatcher.Invoke(() => { RobotErrorText = $"AGV-2: {errorMsg}"; RobotErrorVisibility = Visibility.Visible; }); }
            );
            robot2.CurrentNode = 3;
            robot2.CurrentX = 250;
            robot2.CurrentY = 250;
            Robot2X = 250; Robot2Y = 250;
            robot2.OnPositionChanged += (x, y) => { Application.Current.Dispatcher.Invoke(() => { Robot2X = x; Robot2Y = y; }); };

            // 初始占位申请（按 Zone 级别锁定）
            int zoneId1 = Global.GetZoneId(1);
            int zoneId2 = Global.GetZoneId(3);
            _ = trafficController.WaitAndAcquireLockAsync(zoneId1, "AGV-1");
            _ = trafficController.WaitAndAcquireLockAsync(zoneId2, "AGV-2");

            _robots = new List<BasicRegionNavigation.Core.Interfaces.IRobot> { robot1, robot2 };

            // 实例化 Dispatcher，接管车队
            _taskDispatcher = new BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher(_robots, MapNodes);
            _taskDispatcher.OnTaskCompleted += (order) => 
            {
                Application.Current.Dispatcher.InvokeAsync(async () => 
                {
                    await Task.Delay(1000); // 留出1秒展示"已完成"
                    ActiveTasks.Remove(order);
                });
            };

            // 1. 初始化所有模组 (假设有2个)
            InitializeModules(new[] { "1", "2" });

            // 2. 【核心】单一入口监听
            _modbusService.OnModuleDataChanged += HandleDataChanged;

            InitializeSubscriptions(_modbusService);

        }
        private void StartRealPieDataPolling()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        // 1. 计算当班时间 (逻辑保持不变)
                        var now = DateTime.Now;
                        DateTime start, end;
                        if (now.Hour >= 8 && now.Hour < 20)
                        {
                            start = now.Date.AddHours(8);
                            end = now.Date.AddHours(20);
                        }
                        else
                        {
                            if (now.Hour >= 20)
                            {
                                start = now.Date.AddHours(20);
                                end = now.Date.AddDays(1).AddHours(8);
                            }
                            else
                            {
                                start = now.Date.AddDays(-1).AddHours(20);
                                end = now.Date.AddHours(8);
                            }
                        }

                        // 1. 获取统计字典
                        var allStats = await _productionService.GetProductStatsByModuleAndProjectAsync(start, end);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // 2. 遍历字典
                            foreach (var moduleEntry in allStats)
                            {
                                string moduleId = moduleEntry.Key;       // "1" 或 "2"
                                var projectStats = moduleEntry.Value;    // {"ProjectA": 100, "ProjectB": 50}

                                // 3. 将 Dictionary<string, int> 传给 UI 更新逻辑
                                HandleDataChanged(moduleId, ModuleDataCategory.UpPieInfo, projectStats);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[饼图轮询错误] {ex.Message}");
                    }

                    await Task.Delay(500);
                }
            });
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
        }        // 切换模组的方法 (供前端 ComboBox 绑定)
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
        // 在 ViewAViewModel 类中添加此方法

        [RelayCommand]
        private async Task NavigateModule(string index)
        {
            SwitchModule(index);
        }


        [ObservableProperty]
        private ObservableCollection<ISeries> _revenueSeries;

        [ObservableProperty]
        private int[] _myIntDataArray = new int[] { 10, 50, 25, 60, 90 };


        //// 模组名称属性组
        //[ObservableProperty] private string _model1Name;
        //[ObservableProperty] private string _model2Name;



        public static string[] WarningName = new string[] { /* 省略长列表，保持原样 */ "上料模组1_传感器故障", "..." };

        // ========================== 命令 ==========================

        [RelayCommand]
        private void DispatchTask()
        {
            var random = new Random();
            int index = random.Next(MapNodes.Count);
            int randomId = MapNodes[index].Id;

            var order = new TaskOrder 
            { 
                TargetNodeId = randomId 
            };

            ActiveTasks.Add(order);
            _taskDispatcher.SubmitTask(order);
        }

        // ========================== 死锁与管制区测试命令 ==========================

        private void ClearAndSetRobots(int r1Node, int r2Node)
        {
            // 清理任务队列
            ActiveTasks.Clear();
            ResetRobot();
            
            // 强行清空锁字典
            var trafficCtrl = _robots[0].GetType().GetField("_trafficController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_robots[0]) as BasicRegionNavigation.Core.Interfaces.ITrafficController;
            trafficCtrl?.ClearAllLocks();

            var r1 = _robots[0] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;
            var r2 = _robots[1] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;

            var node1 = MapNodes.FirstOrDefault(n => n.Id == r1Node);
            var node2 = MapNodes.FirstOrDefault(n => n.Id == r2Node);

            r1.CurrentNode = r1Node; r1.CurrentX = node1.X; r1.CurrentY = node1.Y; Robot1X = node1.X; Robot1Y = node1.Y;
            r2.CurrentNode = r2Node; r2.CurrentX = node2.X; r2.CurrentY = node2.Y; Robot2X = node2.X; Robot2Y = node2.Y;

            trafficCtrl?.WaitAndAcquireLockAsync(Global.GetZoneId(r1Node), "AGV-1");
            trafficCtrl?.WaitAndAcquireLockAsync(Global.GetZoneId(r2Node), "AGV-2");
        }

        private async Task ExecuteSequentialTasksAsync(BasicRegionNavigation.Core.Interfaces.IRobot robot, int[] nodeIds, string prefix)
        {
            foreach (var id in nodeIds)
            {
                var order = new TaskOrder { TargetNodeId = id, AssignedRobotId = robot.Id, Status = prefix };
                Application.Current.Dispatcher.Invoke(() => ActiveTasks.Add(order));

                var node = MapNodes.FirstOrDefault(n => n.Id == id);
                if (node != null)
                {
                    await robot.GoToNodeAsync(node);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    order.Status = "已到达/完成";
                    order.IsCompleted = true;
                    // 一秒后自动移除 UI
                    Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        Application.Current.Dispatcher.Invoke(() => ActiveTasks.Remove(order));
                    });
                });
            }
        }

        [RelayCommand]
        private void TestCorridor()
        {
            ClearAndSetRobots(1, 5); // AGV1 在左侧节点1，AGV2 在右侧节点5

            // 派发任务：两车直接向对方所在的端点发起冲锋！
            // 测试目标：验证当终点被占有时，能否触发 联动避让 (驱赶对方去 Node 12 或 Node 4)
            var t1 = new TaskOrder { TargetNodeId = 5 };
            ActiveTasks.Add(t1);
            _taskDispatcher.SubmitTask(t1);

            // 稍微延迟半秒再发第二个任务，使得 AGV1 成为“驱赶者”，AGV2 成为被逼迫进“停车湾”的车辆。
            Task.Run(async () => 
            {
                await Task.Delay(500); 
                var t2 = new TaskOrder { TargetNodeId = 1 };
                Application.Current.Dispatcher.Invoke(() => {
                    ActiveTasks.Add(t2);
                    _taskDispatcher.SubmitTask(t2);
                });
            });
        }

        [RelayCommand]
        private void TestIntersection()
        {
            ClearAndSetRobots(3, 9); // 一辆在上方路口前(Zone 1)，一辆在左侧路口前(单独Zone)

            var r1 = _robots[0] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;
            var r2 = _robots[1] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;

            // R1: 3 -> 8(进入Zone2) -> 11(驶离Zone2)
            // R2: 9 -> 7(进入Zone2) -> 10(驶离Zone2)
            _ = Task.Run(async () => { await ExecuteSequentialTasksAsync(r1, new[] { 8, 11 }, "直行经过路口"); });

            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                await ExecuteSequentialTasksAsync(r2, new[] { 7, 10 }, "横穿经过路口");
            });
        }

        [RelayCommand]
        private void TestDeadlock()
        {
            ClearAndSetRobots(3, 6); // R1 在走廊 Zone 1，R2 在十字路口入口 Zone 2

            var r1 = _robots[0] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;
            var r2 = _robots[1] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;

            // R1 从 Zone 1 冲向 Zone 2； R2 从 Zone 2 冲向 Zone 1
            _ = Task.Run(async () => { await ExecuteSequentialTasksAsync(r1, new[] { 7 }, "互锁冲锋"); });
            _ = Task.Run(async () => { await ExecuteSequentialTasksAsync(r2, new[] { 4 }, "互锁冲锋"); });
        }


        [RelayCommand]
        private void PauseRobot()
        {
            foreach (var r in _robots) (r as BasicRegionNavigation.Infrastructure.Robots.MockRobot)?.Pause();
        }

        [RelayCommand]
        private void ResumeRobot()
        {
            foreach (var r in _robots) (r as BasicRegionNavigation.Infrastructure.Robots.MockRobot)?.Resume();
        }

        [RelayCommand]
        private void CancelRobot()
        {
            foreach (var r in _robots) (r as BasicRegionNavigation.Infrastructure.Robots.MockRobot)?.Cancel();
        }

        [RelayCommand]
        private void ResetRobot()
        {
            foreach (var r in _robots) (r as BasicRegionNavigation.Infrastructure.Robots.MockRobot)?.Reset();
            RobotErrorVisibility = Visibility.Collapsed;
        }

        [RelayCommand]
        private void ShowText(string param)
        {
            MyConfigCommand.configHelper = Global._config;
            MyConfigCommand.ShowText(param);
        }

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


    }

}