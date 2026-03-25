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
        private double _robot3X;

        [ObservableProperty]
        private double _robot3Y;

        [ObservableProperty]
        private string _robot3StateText = "状态: IDLE";

        [ObservableProperty]
        private string _robotPositionText = "坐标实时监控已简化";

        [ObservableProperty]
        private string _robotErrorText;

        [ObservableProperty]
        private Visibility _robotErrorVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private ObservableCollection<LogicNode> _mapNodes;

        [ObservableProperty]
        private ObservableCollection<MapEdgeViewModel> _mapEdges = new ObservableCollection<MapEdgeViewModel>();

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
        private readonly ILoggerService _loggerService;
        private readonly IDatabaseService _databaseService; // 【新增】注入数据库服务
        public ViewAViewModel(IModbusService modbusService, IProductionService productionService, IAlarmHistoryService alarmHistoryService, ILoggerService loggerService, IDatabaseService databaseService)
        {
            _loggerService = loggerService;
            _databaseService = databaseService;
            _modbusService = modbusService;
            _alarmHistoryService = alarmHistoryService;
            _productionService = productionService; // 【新增】赋值


            // 初始化极限测试地图
            InitTestMap();

            TargetNode = MapNodes.FirstOrDefault();

            var trafficController = new BasicRegionNavigation.Applications.Controllers.TrafficController();

            // 实例化两台小车
            var robot1 = new BasicRegionNavigation.Infrastructure.Robots.MockRobot(
                id: "AGV-1",
                trafficController: trafficController,
                logger: _loggerService, // 【新增】传入日志服务
                mapNodes: MapNodes,
                onStateUpdate: (state) => { Application.Current.Dispatcher.Invoke(() => { Robot1StateText = $"状态: {state}"; }); },
                onError: (errorMsg) => { Application.Current.Dispatcher.Invoke(() => { RobotErrorText = $"AGV-1: {errorMsg}"; RobotErrorVisibility = Visibility.Visible; }); }
            );
            // 放在西侧节点 4
            robot1.CurrentNode = 4;
            robot1.CurrentX = 100;
            robot1.CurrentY = 300;
            Robot1X = 100; Robot1Y = 300;
            robot1.OnPositionChanged += (x, y) => { Application.Current.Dispatcher.Invoke(() => { Robot1X = x; Robot1Y = y; }); };

            var robot2 = new BasicRegionNavigation.Infrastructure.Robots.MockRobot(
                id: "AGV-2",
                trafficController: trafficController,
                logger: _loggerService, // 【新增】传入日志服务
                mapNodes: MapNodes,
                onStateUpdate: (state) => { Application.Current.Dispatcher.Invoke(() => { Robot2StateText = $"状态: {state}"; }); },
                onError: (errorMsg) => { Application.Current.Dispatcher.Invoke(() => { RobotErrorText = $"AGV-2: {errorMsg}"; RobotErrorVisibility = Visibility.Visible; }); }
            );
            // 放在东侧节点 3
            robot2.CurrentNode = 3;
            robot2.CurrentX = 500;
            robot2.CurrentY = 300;
            Robot2X = 500; Robot2Y = 300;
            robot2.OnPositionChanged += (x, y) => { Application.Current.Dispatcher.Invoke(() => { Robot2X = x; Robot2Y = y; }); };

            // 初始占位申请（按新方案：直接读取节点属性生成的 ZoneName）
            var startNode1 = MapNodes.First(n => n.Id == 4);
            var startNode2 = MapNodes.First(n => n.Id == 3);
            _ = trafficController.WaitAndAcquireLockAsync(Global.GetZoneName(startNode1), "AGV-1");
            _ = trafficController.WaitAndAcquireLockAsync(Global.GetZoneName(startNode2), "AGV-2");

            _robots = new List<BasicRegionNavigation.Core.Interfaces.IRobot> { robot1, robot2 };

            // 实例化 Dispatcher
            _taskDispatcher = new BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher(_robots, MapNodes, _databaseService, trafficController);
            _taskDispatcher.OnTaskCompleted += (order) => 
            {
                Application.Current.Dispatcher.InvokeAsync(async () => 
                {
                    await Task.Delay(1000); // 留出1秒展示"已完成"
                    ActiveTasks.Remove(order);
                });
            };

            // 初始化模组服务等
            InitializeModules(new[] { "1", "2" });
            _modbusService.OnModuleDataChanged += HandleDataChanged;
            InitializeSubscriptions(_modbusService);
        }

        /// <summary>
        /// 硬编码极限测试地图 (Sprint 1 专用)
        /// 中心十字架构 + 旁路管控区 + 充电区
        /// </summary>
        private void InitTestMap()
        {
            var nodes = new ObservableCollection<LogicNode>
    {
        // 1. 中心十字交汇区
        new LogicNode { Id = 0, X = 300, Y = 300 },
        new LogicNode { Id = 1, X = 300, Y = 180 },
        new LogicNode { Id = 2, X = 300, Y = 500 },
        new LogicNode { Id = 3, X = 500, Y = 300 },
        new LogicNode { Id = 4, X = 100, Y = 300 },

        // 2. 旁路与管制区 (Zone_A)
        new LogicNode { Id = 5, X = 100, Y = 100, ZoneName = "Zone_A" },
        new LogicNode { Id = 8, X = 233, Y = 100, ZoneName = "Zone_A" },
        new LogicNode { Id = 9, X = 366, Y = 100, ZoneName = "Zone_A" },
        new LogicNode { Id = 6, X = 500, Y = 100, ZoneName = "Zone_A" },

        // 3. 充电/安全区
        new LogicNode { Id = 7, X = 500, Y = 500, NodeType = "Charging" },

        // 4. 外围扩展区 (测试用)
        new LogicNode { Id = 10, X = 650, Y = 300, NodeType = "Parking" }
    };

            // 局部辅助函数，安全获取节点
            LogicNode Get(int id) => nodes.First(n => n.Id == id);

            // 中心节点十字连接（双向通行）
            AddBidirectionalEdge(Get(0), Get(1));
            AddBidirectionalEdge(Get(0), Get(2));
            AddBidirectionalEdge(Get(0), Get(3));
            AddBidirectionalEdge(Get(0), Get(4));

            // 外围管制环线（严格单向逆时针通行） (4 -> 5 -> 8 -> 9 -> 6)
            AddDirectedEdge(Get(4), Get(5));
            AddDirectedEdge(Get(5), Get(8));
            AddDirectedEdge(Get(8), Get(9));
            AddDirectedEdge(Get(9), Get(6));
            
            // 出口单行道陷阱（关键测试点）：在节点 6 和节点 3 之间，只允许从 6 走到 3
            AddDirectedEdge(Get(6), Get(3));

            // 充电桩 (双向通行，仅连南侧)
            AddBidirectionalEdge(Get(2), Get(7));

            // 外围节点 (仅连节点 3)
            AddBidirectionalEdge(Get(3), Get(10));

            MapNodes = nodes;
            GenerateEdges(nodes);
        }

        /// <summary>
        /// 根据节点连接关系动态生成 UI 连线集合（支持单向箭头）
        /// </summary>
        private void GenerateEdges(IEnumerable<LogicNode> nodes)
        {
            var edges = new List<MapEdgeViewModel>();
            var processed = new HashSet<string>(); // 用于去重

            foreach (var nodeA in nodes)
            {
                foreach (var nextId in nodeA.ConnectedNodeIds)
                {
                    var nodeB = nodes.FirstOrDefault(n => n.Id == nextId);
                    if (nodeB == null) continue;

                    // 唯一标识一条连线（不分方向）用于去重
                    string edgeKey = nodeA.Id < nodeB.Id ? $"{nodeA.Id}-{nodeB.Id}" : $"{nodeB.Id}-{nodeA.Id}";

                    bool aToB = true; 
                    bool bToA = nodeB.ConnectedNodeIds.Contains(nodeA.Id);

                    if (bToA)
                    {
                        // 双向线：只需画一次
                        if (!processed.Contains(edgeKey))
                        {
                            edges.Add(new MapEdgeViewModel(nodeA.X, nodeA.Y, nodeB.X, nodeB.Y, false));
                            processed.Add(edgeKey);
                        }
                    }
                    else
                    {
                        // 单向线：直接添加且不参与双向去重（如果 A->B 是单向，B->A 可能不存在，也可能是另一个单向）
                        edges.Add(new MapEdgeViewModel(nodeA.X, nodeA.Y, nodeB.X, nodeB.Y, true));
                    }
                }
            }
            MapEdges = new ObservableCollection<MapEdgeViewModel>(edges);
        }

        private void AddBidirectionalEdge(LogicNode a, LogicNode b)
        {
            if (!a.ConnectedNodeIds.Contains(b.Id)) a.ConnectedNodeIds.Add(b.Id);
            if (!b.ConnectedNodeIds.Contains(a.Id)) b.ConnectedNodeIds.Add(a.Id);
        }

        private void AddDirectedEdge(LogicNode from, LogicNode to)
        {
            if (!from.ConnectedNodeIds.Contains(to.Id))
            {
                from.ConnectedNodeIds.Add(to.Id);
            }
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

            trafficCtrl?.WaitAndAcquireLockAsync(Global.GetZoneName(node1), "AGV-1");
            trafficCtrl?.WaitAndAcquireLockAsync(Global.GetZoneName(node2), "AGV-2");
        }

        private async Task ExecuteSequentialTasksAsync(BasicRegionNavigation.Core.Interfaces.IRobot robot, int[] nodeIds, string prefix)
        {
            foreach (var id in nodeIds)
            {
                var order = new TaskOrder { TargetNodeId = id, AssignedRobotId = robot.Id, Status = TaskStatus.Executing, StageDescription = prefix };
                Application.Current.Dispatcher.Invoke(() => ActiveTasks.Add(order));

                var node = MapNodes.FirstOrDefault(n => n.Id == id);
                if (node != null)
                {
                    await robot.GoToNodeAsync(node);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    order.Status = TaskStatus.Completed;
                    order.StageDescription = "已到达";
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
            // 测试目标：验证区域锁 (Zone_A) 和旁路路径
            // AGV-1 从西侧(4)前往东侧(3), AGV-2 从东北(6)前往西北(5)
            ClearAndSetRobots(4, 6); 

            var t1 = new TaskOrder { TargetNodeId = 3, AssignedRobotId = "AGV-1" };
            ActiveTasks.Add(t1);
            _taskDispatcher.SubmitTask(t1);

            Task.Run(async () => 
            {
                await Task.Delay(500); 
                var t2 = new TaskOrder { TargetNodeId = 5, AssignedRobotId = "AGV-2" };
                Application.Current.Dispatcher.Invoke(() => {
                    ActiveTasks.Add(t2);
                    _taskDispatcher.SubmitTask(t2);
                });
            });
        }

        [RelayCommand]
        private void TestIntersection()
        {
            // 测试目标：十字路口相交寻路与管制
            // R1: 北(1) -> 南(2); R2: 西(4) -> 东(3)
            ClearAndSetRobots(1, 4); 

            var r1 = _robots[0] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;
            var r2 = _robots[1] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;

            _ = Task.Run(async () => { await ExecuteSequentialTasksAsync(r1, new[] { 0, 2 }, "南北贯穿"); });

            _ = Task.Run(async () =>
            {
                await Task.Delay(800);
                await ExecuteSequentialTasksAsync(r2, new[] { 0, 3 }, "东西横跨");
            });
        }

        [RelayCommand]
        private void TestDeadlock()
        {
            // 测试目标：中心点死锁冲锋
            // R1(北) 和 R2(南) 同时冲向中心点(0)
            ClearAndSetRobots(1, 2); 

            var r1 = _robots[0] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;
            var r2 = _robots[1] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;

            _ = Task.Run(async () => { await ExecuteSequentialTasksAsync(r1, new[] { 0 }, "抢夺中心点"); });
            _ = Task.Run(async () => { await ExecuteSequentialTasksAsync(r2, new[] { 0 }, "抢夺中心点"); });
        }

        [RelayCommand]
        private async Task RunTestOne_ZoneQueueing()
        {
            // ============================================================
            // 第 1 步：重置沙盘 (Reset)
            // ============================================================
            // 1.1 清理 UI 任务列表
            ActiveTasks.Clear();

            // 1.2 获取流量控制器并强制清除所有底层残留锁
            // (利用反射获取内部私有字段 _trafficController，确保干净重置)
            var trafficCtrl = _robots[0].GetType().GetField("_trafficController", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(_robots[0]) as BasicRegionNavigation.Core.Interfaces.ITrafficController;
            trafficCtrl?.ClearAllLocks();

            // 1.3 重新实例化调度器以清空队列与派发缓存 (彻底清空大脑)
            _taskDispatcher = new BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher(_robots, MapNodes, _databaseService, trafficCtrl);
            _taskDispatcher.OnTaskCompleted += (order) =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1000); // 预留完成状态展示时间
                    ActiveTasks.Remove(order);
                });
            };

            // ============================================================
            // 第 2 步：初始化特定测试车辆 (Init Robots)
            // ============================================================
            var r1 = _robots[0] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;
            var r2 = _robots[1] as BasicRegionNavigation.Infrastructure.Robots.MockRobot;

            // 强制恢复机器人运行状态，排除 ERROR 干扰
            r1.Reset(); r2.Reset();

            var node4 = MapNodes.FirstOrDefault(n => n.Id == 4);
            var node5 = MapNodes.FirstOrDefault(n => n.Id == 5);

            // 强行闪现到测试起点 (R1 在 4 号点外，R2 在 5 号点内)
            r1.CurrentNode = 4; r1.CurrentX = node4.X; r1.CurrentY = node4.Y; Robot1X = node4.X; Robot1Y = node4.Y;
            r2.CurrentNode = 5; r2.CurrentX = node5.X; r2.CurrentY = node5.Y; Robot2X = node5.X; Robot2Y = node5.Y;

            // 重要：由于 R2 初始已经在管制区内部（Node 5），必须手动为其补上区域锁，模拟“正在占用”状态
            // 否则 R1 会在 R2 还没动时就能非法闯入同一区域
            if (trafficCtrl != null)
            {
                await trafficCtrl.WaitAndAcquireLockAsync(Global.GetZoneName(node5), "AGV-2");
            }

            // ============================================================
            // 第 3 步：下发测试任务 (Dispatch Tasks)
            // ============================================================
            // 3.1 下发任务 R2 (从 5 号点离开管制区去 3 号点)
            var task2 = new TaskOrder { StartNodeId = 5, TargetNodeId = 3 };
            ActiveTasks.Add(task2);
            _taskDispatcher.SubmitTask(task2);

            // 等待 200ms 确保调度引擎先处理 R2，拿到先后顺序
            await Task.Delay(200);

            // 3.2 下发任务 R1 (试图从 4 号点进入管制区去 6 号点)
            // 预期结果：R1 会在 Node 4 停住，等待 R2 释放 Zone_A 的锁后方能通行
            var task1 = new TaskOrder { StartNodeId = 4, TargetNodeId = 6 };
            ActiveTasks.Add(task1);
            _taskDispatcher.SubmitTask(task1);
        }

        /// <summary>
        /// 核心测试二：就近分配与搬运任务验证
        /// 
        /// 【预期现象】
        /// 1. 三台车同时空闲：R1(Node4, 100,300), R2(Node0, 300,300), R3(Node3, 500,300)
        /// 2. 下发任务：StartNodeId=6(500,100), TargetNodeId=7(500,500)
        /// 3. 调度引擎计算每辆车到起点 Node6 的直线距离：
        ///    - R1(100,300) → Node6(500,100): √(400²+200²) ≈ 447
        ///    - R2(300,300) → Node6(500,100): √(200²+200²) ≈ 283
        ///    - R3(500,300) → Node6(500,100): √(0²+200²) = 200  ← 最近！
        /// 4. 因此只有 R3 会被选中，先移动到 Node6 取货，再移动到 Node7 卸货
        /// 5. R1 和 R2 全程保持静止不动
        /// </summary>
        [RelayCommand]
        private async Task RunTestTwo_NearestAllocation()
        {
            // ============================================================
            // 第 1 步：重置沙盘 (Reset)
            // ============================================================
            ActiveTasks.Clear();

            // ============================================================
            // 第 2 步：初始化 2 台测试车辆 (Init 2 Robots)
            // ============================================================
            var trafficController = new BasicRegionNavigation.Applications.Controllers.TrafficController();

            // R1: 外围 Node10 (650,300) - 距离起点较远 (250px)
            var robot1 = new BasicRegionNavigation.Infrastructure.Robots.MockRobot(
                id: "AGV-1",
                trafficController: trafficController,
                logger: _loggerService,
                mapNodes: MapNodes,
                onStateUpdate: (state) => { Application.Current.Dispatcher.Invoke(() => { Robot1StateText = $"状态: {state}"; }); },
                onError: (errorMsg) => { Application.Current.Dispatcher.Invoke(() => { RobotErrorText = $"AGV-1: {errorMsg}"; RobotErrorVisibility = Visibility.Visible; }); }
            );
            var node10 = MapNodes.FirstOrDefault(n => n.Id == 10);
            robot1.CurrentNode = 10; robot1.CurrentX = node10.X; robot1.CurrentY = node10.Y;
            Robot1X = node10.X; Robot1Y = node10.Y;
            robot1.OnPositionChanged += (x, y) => { Application.Current.Dispatcher.Invoke(() => { Robot1X = x; Robot1Y = y; }); };

            // R2: 北侧 Node1 (300,180) - 距离起点最近 (215px)
            var robot2 = new BasicRegionNavigation.Infrastructure.Robots.MockRobot(
                id: "AGV-2",
                trafficController: trafficController,
                logger: _loggerService,
                mapNodes: MapNodes,
                onStateUpdate: (state) => { Application.Current.Dispatcher.Invoke(() => { Robot2StateText = $"状态: {state}"; }); },
                onError: (errorMsg) => { Application.Current.Dispatcher.Invoke(() => { RobotErrorText = $"AGV-2: {errorMsg}"; RobotErrorVisibility = Visibility.Visible; }); }
            );
            var node1 = MapNodes.FirstOrDefault(n => n.Id == 1);
            robot2.CurrentNode = 1; robot2.CurrentX = node1.X; robot2.CurrentY = node1.Y;
            Robot2X = node1.X; Robot2Y = node1.Y;
            robot2.OnPositionChanged += (x, y) => { Application.Current.Dispatcher.Invoke(() => { Robot2X = x; Robot2Y = y; }); };

            // 隐藏 R3 (不参与本次测试)
            Robot3X = -100; Robot3Y = -100;

            // 重组车辆集合 (2 台参与就近分配竞标)
            _robots = new List<BasicRegionNavigation.Core.Interfaces.IRobot> { robot1, robot2 };

            // 重新实例化调度器 (传入 2 台车)
            _taskDispatcher = new BasicRegionNavigation.Applications.Dispatchers.TaskDispatcher(_robots, MapNodes, _databaseService, trafficController);
            _taskDispatcher.OnTaskCompleted += (order) =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(1000);
                    ActiveTasks.Remove(order);
                });
            };

            // ============================================================
            // 第 3 步：下发单个搬运任务 (Dispatch Single Task)
            // ============================================================
            // 任务：从 Node6(东北角 500,100) 取货 → Node7(东南角 500,500) 卸货
            // 由于 R3 在 Node3(500,300)，距离 Node6 仅 200px，是最近的车
            var task = new TaskOrder { StartNodeId = 6, TargetNodeId = 7 };
            ActiveTasks.Add(task);
            _taskDispatcher.SubmitTask(task);

            // "就近分配"算法应自动选择 R3，界面上可以看到：
            //   - R3 先向北移动到 Node6 (取货)
            //   - 停留 2 秒 (模拟装货)
            //   - 再向南移动到 Node7 (卸货)
            //   - R1 和 R2 全程不动
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