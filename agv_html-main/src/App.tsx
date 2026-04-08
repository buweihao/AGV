/**
 * @license
 * SPDX-License-Identifier: Apache-2.0
 */

import React, { useState, useEffect, useRef, useMemo } from 'react';
import {
  Battery, BatteryMedium, BatteryWarning,
  Map as MapIcon, Crosshair, Navigation,
  Play, Pause, Square, RotateCcw, Unlock, AlertTriangle,
  Terminal, Send, Activity, Lock, List, Cpu, Monitor,
  ZoomIn, ZoomOut, Maximize, Layers, Wifi, CheckCircle2, Search,
  LayoutDashboard, ClipboardList, Settings, Database, Users, Truck
} from 'lucide-react';
import MapEditor from './components/MapEditor';

// 生成模拟的几十辆 AGV 数据
const generateMockAGVs = () => {
  return Array.from({ length: 50 }, (_, i) => {
    const id = i + 1;
    const isCharging = i % 8 === 0;
    const isError = i % 15 === 0;
    const isIdle = i % 4 === 0 && !isCharging && !isError;

    return {
      id: `AGV-${id.toString().padStart(3, '0')}`,
      status: isError ? '异常报警' : isCharging ? '充电中' : isIdle ? '待命' : '正在导航',
      battery: isCharging ? Math.floor(Math.random() * 40) + 10 : Math.floor(Math.random() * 80) + 20,
      isCharging: isCharging,
      currentTask: isIdle ? '无任务' : isCharging ? '前往充电桩' : `搬运物料至 LM${Math.floor(Math.random() * 100)}`,
      heldLocks: i % 5 === 0 && !isIdle ? [`区段${String.fromCharCode(65 + Math.floor(Math.random() * 5))}`] : [],
      x: Math.random() * 1400 + 100, // 适应大地图的坐标系
      y: Math.random() * 800 + 100,
      angle: Math.random() * Math.PI * 2,
      speed: isIdle || isCharging || isError ? 0 : (Math.random() * 1.2 + 0.3),
      liftAngle: i % 3 === 0 ? 90 : 0,
      logs: [
        { time: new Date(Date.now() - 5000).toLocaleTimeString(), msg: `[SYS] 车辆心跳正常` },
        { time: new Date().toLocaleTimeString(), msg: `[CMD] 保持寄存器同步完成` }
      ]
    };
  });
};

export default function App() {
  // === 屏幕缩放自适应逻辑 ===
  const screenRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(1);

  useEffect(() => {
    const handleResize = () => {
      if (screenRef.current && screenRef.current.parentElement) {
        const parent = screenRef.current.parentElement;
        const { width, height } = parent.getBoundingClientRect();
        // 按照 1920x1080 计算缩放比，保持比例
        const scaleX = width / 1920;
        const scaleY = height / 1080;
        setScale(Math.min(scaleX, scaleY) * 0.98); // 留出一点边距
      }
    };
    window.addEventListener('resize', handleResize);
    handleResize(); // 初始化
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  // === 视图导航状态 ===
  const [currentView, setCurrentView] = useState('monitoring');

  // === 系统数据与状态 ===
  const [agvs, setAgvs] = useState<any[]>([]);
  const [tasks, setTasks] = useState<any[]>([]);
  const [selectedTaskId, setSelectedTaskId] = useState<string>("");
  const [mapData, setMapData] = useState({ nodes: [] as any[], edges: [] as any[] });
  const [selectedAgvId, setSelectedAgvId] = useState("");
  const [debugCmd, setDebugCmd] = useState({ type: '0x', address: '00004', value: '1' });
  const [currentTime, setCurrentTime] = useState(new Date());

  useEffect(() => {
    const timer = setInterval(() => setCurrentTime(new Date()), 1000);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    const handleMessage = (event: any) => {
      try {
        const payload = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
        if (payload.type === 'robot_update') {
          setAgvs(payload.data || []);

          // 若之前没有选择，默认选第一个
          setSelectedAgvId(prev => {
            if (!prev && payload.data && payload.data.length > 0) return payload.data[0].id;
            return prev;
          });
        } else if (payload.type === 'map_update') {
          setMapData({ nodes: payload.nodes || [], edges: payload.edges || [] });
        } else if (payload.type === 'task_update') {
          setTasks(payload.data || []);
          setSelectedTaskId(prev => {
            if (!prev && payload.data && payload.data.length > 0) return payload.data[0].id;
            return prev;
          });
        }
      } catch (e) {
        console.error("解析WPF消息报错", e);
      }
    };

    if ((window as any).chrome?.webview) {
      (window as any).chrome.webview.addEventListener('message', handleMessage);
      return () => {
        (window as any).chrome.webview.removeEventListener('message', handleMessage);
      };
    }
  }, []);

  const selectedAgv = useMemo(() => {
    if (agvs.length === 0) {
      return {
        id: '---', status: '等待数据', battery: 0, isCharging: false,
        currentTask: '...', heldLocks: [], x: 0, y: 0, angle: 0, speed: 0, liftAngle: 0, logs: []
      };
    }
    return agvs.find(a => a.id === selectedAgvId) || agvs[0];
  }, [agvs, selectedAgvId]);

  const mapBounds = useMemo(() => {
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;

    const update = (x: number, y: number) => {
      if (typeof x === 'number') {
        minX = Math.min(minX, x);
        maxX = Math.max(maxX, x);
      }
      if (typeof y === 'number') {
        minY = Math.min(minY, y);
        maxY = Math.max(maxY, y);
      }
    };

    mapData.nodes.forEach(n => update(n.x, n.y));
    mapData.edges.forEach(e => {
      update(e.x1, e.y1);
      update(e.x2, e.y2);
    });

    // 兜底逻辑
    if (minX === Infinity) return { w: 2000, h: 1500, minX: 0, minY: 0 };

    const padding = 200;
    return {
      w: (maxX - minX) + padding * 2,
      h: (maxY - minY) + padding * 2,
      minX: minX - padding,
      minY: minY - padding
    };
  }, [mapData]);

  // === 地图缩放与拖拽逻辑 ===
  const [mapZoom, setMapZoom] = useState(0.8);
  const [mapPan, setMapPan] = useState({ x: 0, y: 0 });
  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState({ x: 0, y: 0 });

  const handleMapWheel = (e: React.WheelEvent) => {
    // 根据滚轮方向计算缩放
    const zoomDelta = e.deltaY > 0 ? -0.1 : 0.1;
    setMapZoom(prev => Math.max(0.2, Math.min(prev + zoomDelta, 3))); // 限制缩放级别 0.2x - 3x
  };

  const handleMapDragStart = (e: React.MouseEvent) => {
    setIsDragging(true);
    setDragStart({ x: e.clientX - mapPan.x, y: e.clientY - mapPan.y });
  };

  const handleMapDrag = (e: React.MouseEvent) => {
    if (!isDragging) return;
    setMapPan({ x: e.clientX - dragStart.x, y: e.clientY - dragStart.y });
  };

  const handleMapDragEnd = () => setIsDragging(false);

  const resetMap = () => {
    setMapZoom(0.8);
    setMapPan({ x: 0, y: 0 });
  };

  // === 控制交互 ===
  const handleControl = (action: string) => {
    setAgvs(prevAgvs => prevAgvs.map(agv => {
      if (agv.id === selectedAgvId) {
        const newLog = { time: new Date().toLocaleTimeString(), msg: `[OP] 下发快捷控制: ${action}` };
        return {
          ...agv,
          status: action === '暂停' ? '导航暂停' : action === '继续' ? '正在导航' : agv.status,
          logs: [newLog, ...agv.logs].slice(0, 50)
        };
      }
      return agv;
    }));
  };

  const handleDebugSend = () => {
    setAgvs(prevAgvs => prevAgvs.map(agv => {
      if (agv.id === selectedAgvId) {
        const newLog = {
          time: new Date().toLocaleTimeString(),
          msg: `[Modbus TX] 写 ${debugCmd.type} 寄存器, 地址: ${debugCmd.address}, 值: ${debugCmd.value}`
        };
        return { ...agv, logs: [newLog, ...agv.logs].slice(0, 50) };
      }
      return agv;
    }));
  };

  // 渲染单车电量图标
  const renderBatteryIcon = (battery: number) => {
    if (battery > 60) return <Battery className="text-emerald-400 w-10 h-10" />;
    if (battery > 20) return <BatteryMedium className="text-amber-400 w-10 h-10" />;
    return <BatteryWarning className="text-rose-500 w-10 h-10 animate-pulse" />;
  };

  return (
    // 外围环境：代表真实的物理世界（办公桌、墙壁等），这里使用深色渐变
    <div className="flex h-screen w-full bg-gradient-to-b from-slate-700 to-slate-900 items-center justify-center overflow-hidden font-sans">

      {/* 物理显示器模型外壳 */}
      <div className="relative flex flex-col items-center justify-center w-full h-full p-4" ref={screenRef}>

        {/* 屏幕黑边框 */}
        <div className="bg-black p-2 rounded-[20px] shadow-[0_30px_60px_rgba(0,0,0,0.8)] border border-slate-800 relative z-10">

          {/* 内屏 1920x1080 缩放容器 */}
          <div
            className="overflow-hidden bg-slate-900 relative rounded-lg"
            style={{
              width: 1920,
              height: 1080,
              transform: `scale(${scale})`,
              transformOrigin: 'center center'
            }}
          >

            {/* ===================== 软件主界面开始 (1920x1080 实际像素) ===================== */}
            <div className="flex flex-col w-full h-full text-slate-200">

              {/* === 顶部导航栏 (高度 80px) === */}
              <div className="h-[80px] bg-slate-950 border-b border-slate-800 flex items-center justify-between px-8 shrink-0 shadow-md z-20">
                <div className="flex items-center gap-4">
                  <div className="w-12 h-12 bg-cyan-600 rounded-lg flex items-center justify-center shadow-[0_0_15px_rgba(8,145,178,0.5)]">
                    <Monitor className="w-7 h-7 text-white" />
                  </div>
                  <div>
                    <h1 className="text-2xl font-bold tracking-wider text-slate-100">Intelligent AGV Scheduling System</h1>
                    <div className="text-sm text-cyan-500 font-medium">全局调度监控主界面 v3.2.0</div>
                  </div>
                </div>

                <div className="flex items-center gap-8">
                  <div className="flex items-center gap-6 bg-slate-900 px-6 py-2 rounded-full border border-slate-800">
                    <div className="flex items-center gap-2"><CheckCircle2 className="w-5 h-5 text-emerald-500" /> <span className="text-lg">在线: {agvs.length}</span></div>
                    <div className="flex items-center gap-2"><AlertTriangle className="w-5 h-5 text-rose-500" /> <span className="text-lg">报警: {agvs.filter(a => a.status === '异常报警').length}</span></div>
                    <div className="flex items-center gap-2"><Wifi className="w-5 h-5 text-cyan-500" /> <span className="text-lg">网络延时: 12ms</span></div>
                  </div>
                  <div className="text-2xl font-mono font-bold tracking-widest text-slate-300">
                    {currentTime.toLocaleTimeString()}
                  </div>
                </div>
              </div>

              {/* === 主体三栏布局 (高度 1000px) === */}
              <div className="flex-1 flex overflow-hidden">

                {/* ---------------- 最左侧：全局导航栏 (宽 80px) ---------------- */}
                <div className="w-[80px] bg-slate-950 border-r border-slate-800 flex flex-col items-center py-6 gap-6 z-20 shrink-0 shadow-2xl">
                  <button
                    onClick={() => setCurrentView('monitoring')}
                    className={`p-3 rounded-xl transition-all duration-300 relative group ${currentView === 'monitoring' ? 'bg-cyan-600 text-white shadow-[0_0_15px_rgba(8,145,178,0.5)]' : 'text-slate-400 hover:bg-slate-800 hover:text-slate-200'}`}
                    title="全局监控"
                  >
                    <LayoutDashboard className="w-7 h-7" />
                  </button>
                  <button
                    onClick={() => setCurrentView('tasks')}
                    className={`p-3 rounded-xl transition-all duration-300 relative group ${currentView === 'tasks' ? 'bg-cyan-600 text-white shadow-[0_0_15px_rgba(8,145,178,0.5)]' : 'text-slate-400 hover:bg-slate-800 hover:text-slate-200'}`}
                    title="任务调度"
                  >
                    <ClipboardList className="w-7 h-7" />
                  </button>
                  <button
                    onClick={() => setCurrentView('data')}
                    className={`p-3 rounded-xl transition-all duration-300 relative group ${currentView === 'data' ? 'bg-cyan-600 text-white shadow-[0_0_15px_rgba(8,145,178,0.5)]' : 'text-slate-400 hover:bg-slate-800 hover:text-slate-200'}`}
                    title="数据中心"
                  >
                    <Database className="w-7 h-7" />
                  </button>
                  <button
                    onClick={() => setCurrentView('map_editor')}
                    className={`p-3 rounded-xl transition-all duration-300 relative group ${currentView === 'map_editor' ? 'bg-cyan-600 text-white shadow-[0_0_15px_rgba(8,145,178,0.5)]' : 'text-slate-400 hover:bg-slate-800 hover:text-slate-200'}`}
                    title="地图配置"
                  >
                    <MapIcon className="w-7 h-7" />
                  </button>
                  <div className="flex-1"></div>
                  <button
                    onClick={() => setCurrentView('settings')}
                    className={`p-3 rounded-xl transition-all duration-300 relative group ${currentView === 'settings' ? 'bg-cyan-600 text-white shadow-[0_0_15px_rgba(8,145,178,0.5)]' : 'text-slate-400 hover:bg-slate-800 hover:text-slate-200'}`}
                    title="系统设置"
                  >
                    <Settings className="w-7 h-7" />
                  </button>
                </div>

                {currentView === 'map_editor' ? (
                  <MapEditor />
                ) : (
                  <>
                    {/* ---------------- 左侧面板 (动态内容) ---------------- */}
                    {currentView === 'monitoring' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-r border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md">

                        {/* 车辆选择器 */}
                        <div className="p-6 border-b border-slate-800 bg-slate-950/50">
                          <h2 className="text-lg font-bold text-slate-400 mb-3 flex items-center gap-2">
                            <Search className="w-5 h-5" /> 目标车辆定位
                          </h2>
                          <select
                            className="w-full bg-slate-800 border-2 border-slate-700 text-xl text-cyan-300 rounded-xl px-4 py-4 focus:outline-none focus:border-cyan-500 cursor-pointer shadow-inner"
                            value={selectedAgvId}
                            onChange={(e) => setSelectedAgvId(e.target.value)}
                          >
                            {agvs.map(agv => (
                              <option key={agv.id} value={agv.id}>
                                {agv.id} - [{agv.status}] - 电量 {agv.battery}%
                              </option>
                            ))}
                          </select>
                        </div>

                        <div className="p-6 flex-1 overflow-y-auto custom-scrollbar flex flex-col gap-6">
                          {/* 核心状态区块 */}
                          <div>
                            <div className="flex items-center gap-2 mb-4">
                              <Activity className="w-6 h-6 text-cyan-400" />
                              <h2 className="text-xl font-bold text-slate-100">单车核心状态</h2>
                            </div>

                            <div className="bg-slate-800/80 p-6 rounded-2xl border border-slate-700 shadow-lg relative overflow-hidden">
                              {/* 装饰光效 */}
                              <div className={`absolute top-0 left-0 w-1 h-full ${selectedAgv.status === '异常报警' ? 'bg-rose-500' : selectedAgv.status.includes('暂停') ? 'bg-amber-400' : 'bg-emerald-400'}`}></div>

                              <div className="flex justify-between items-center mb-6 border-b border-slate-700 pb-4">
                                <span className="text-4xl font-black text-white tracking-wider font-mono">{selectedAgv.id}</span>
                                <div className={`px-4 py-2 rounded-full border ${selectedAgv.status === '异常报警' ? 'bg-rose-900/30 border-rose-500 text-rose-400' :
                                    selectedAgv.status.includes('暂停') ? 'bg-amber-900/30 border-amber-500 text-amber-400' :
                                      'bg-emerald-900/30 border-emerald-500 text-emerald-400'
                                  } font-bold text-lg shadow-sm`}>
                                  {selectedAgv.status}
                                </div>
                              </div>

                              <div className="flex items-center justify-between mb-6 bg-slate-900 p-4 rounded-xl">
                                <div className="flex items-center gap-4">
                                  {renderBatteryIcon(selectedAgv.battery)}
                                  <div>
                                    <div className="text-slate-400 mb-1">动力电池电量</div>
                                    <div className="text-3xl font-bold font-mono text-slate-100">{selectedAgv.battery}%</div>
                                  </div>
                                </div>
                                {selectedAgv.isCharging && <span className="bg-emerald-600 text-white px-3 py-1 rounded text-sm font-bold animate-pulse">充电中</span>}
                              </div>

                              <div className="space-y-4">
                                <div className="bg-slate-900/50 p-3 rounded-lg border border-slate-700">
                                  <div className="text-sm text-slate-400 mb-2 flex items-center gap-2"><Navigation className="w-4 h-4" /> 正在执行任务</div>
                                  <div className="text-lg font-bold text-cyan-300 truncate">
                                    {selectedAgv.currentTask}
                                  </div>
                                </div>
                                <div className="bg-slate-900/50 p-3 rounded-lg border border-slate-700">
                                  <div className="text-sm text-slate-400 mb-2 flex items-center gap-2"><Lock className="w-4 h-4" /> 交通管制持锁状态</div>
                                  <div className="flex flex-wrap gap-2">
                                    {selectedAgv.heldLocks.length > 0 ? selectedAgv.heldLocks.map((lock, idx) => (
                                      <span key={idx} className="bg-rose-900/80 text-rose-200 border border-rose-700 px-3 py-1 rounded-md text-sm font-medium">
                                        {lock}
                                      </span>
                                    )) : <span className="text-slate-500 text-sm">未持有任何交通锁</span>}
                                  </div>
                                </div>
                              </div>
                            </div>
                          </div>

                          {/* 遥测数据区块 */}
                          <div className="flex-1 flex flex-col">
                            <div className="flex items-center gap-2 mb-4">
                              <Cpu className="w-6 h-6 text-indigo-400" />
                              <h2 className="text-xl font-bold text-slate-100">实时遥测数据</h2>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                              <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700">
                                <div className="text-sm text-slate-400 mb-1">X 绝对坐标 (m)</div>
                                <div className="text-2xl font-mono text-slate-100">{(selectedAgv.x / 100).toFixed(3)}</div>
                              </div>
                              <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700">
                                <div className="text-sm text-slate-400 mb-1">Y 绝对坐标 (m)</div>
                                <div className="text-2xl font-mono text-slate-100">{(selectedAgv.y / 100).toFixed(3)}</div>
                              </div>
                              <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700">
                                <div className="text-sm text-slate-400 mb-1">航向角 / RAD</div>
                                <div className="text-2xl font-mono text-slate-100">{(selectedAgv.angle ?? 0).toFixed(3)}</div>
                              </div>
                              <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700">
                                <div className="text-sm text-slate-400 mb-1">线速度 (m/s)</div>
                                <div className="text-2xl font-mono text-slate-100">{(selectedAgv.speed ?? 0).toFixed(2)}</div>
                              </div>
                              <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700 col-span-2 flex justify-between items-center">
                                <div>
                                  <div className="text-sm text-slate-400 mb-1">顶升机构角度</div>
                                  <div className="text-2xl font-mono text-slate-100">{(selectedAgv.liftAngle ?? 0).toFixed(1)}°</div>
                                </div>
                                <div className="w-12 h-12 bg-slate-900 rounded-full border-4 border-slate-700 flex items-center justify-center relative">
                                  <div className="w-1 h-6 bg-cyan-400 absolute bottom-1/2 origin-bottom rounded-full" style={{ transform: `rotate(${selectedAgv.liftAngle}deg)` }}></div>
                                </div>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}

                    {currentView === 'tasks' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-r border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md p-6">
                        <div className="flex items-center gap-2 mb-6 border-b border-slate-700 pb-4">
                          <ClipboardList className="w-6 h-6 text-cyan-400" />
                          <h2 className="text-xl font-bold text-slate-100">任务调度队列</h2>
                        </div>
                        <div className="flex-1 overflow-y-auto custom-scrollbar space-y-4">
                          {tasks.length > 0 ? tasks.map((task, i) => (
                            <div key={task.id || i} onClick={() => setSelectedTaskId(task.id)} className={`bg-slate-800/80 p-4 rounded-xl border ${selectedTaskId === task.id ? 'border-cyan-500 shadow-[0_0_15px_rgba(34,211,238,0.2)]' : 'border-slate-700 hover:border-cyan-500/50'} transition-all cursor-pointer`}>
                              <div className="flex justify-between items-center mb-2">
                                <span className="font-bold text-slate-200">{task.id}</span>
                                <span className={`text-xs px-2 py-1 rounded border ${task.statusText === '执行中' ? 'bg-emerald-900/50 text-emerald-400 border-emerald-800' : 'bg-amber-900/50 text-amber-400 border-amber-800'}`}>{task.statusText}</span>
                              </div>
                              <div className="text-sm text-slate-400 mb-1">承运车辆: <span className="text-slate-200 font-mono">{task.robotId}</span></div>
                              <div className="text-sm text-slate-400">当前阶段: <span className="text-slate-200">{task.stage || '等待执行'}</span></div>
                              <div className="mt-3 w-full bg-slate-900 rounded-full h-1.5">
                                <div className="bg-cyan-500 h-1.5 rounded-full" style={{ width: task.isCompleted ? '100%' : '50%' }}></div>
                              </div>
                            </div>
                          )) : (
                            <div className="text-slate-500 text-center py-10">暂无活动任务</div>
                          )}
                        </div>
                      </div>
                    )}

                    {currentView === 'data' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-r border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md p-6">
                        <div className="flex items-center gap-2 mb-6 border-b border-slate-700 pb-4">
                          <Database className="w-6 h-6 text-cyan-400" />
                          <h2 className="text-xl font-bold text-slate-100">数据统计中心</h2>
                        </div>
                        <div className="space-y-6">
                          <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700">
                            <div className="text-sm text-slate-400 mb-2">今日完成任务数</div>
                            <div className="text-4xl font-mono text-cyan-400 font-bold">1,284</div>
                          </div>
                          <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700">
                            <div className="text-sm text-slate-400 mb-2">平均运行效率 (OEE)</div>
                            <div className="text-4xl font-mono text-emerald-400 font-bold">92.5%</div>
                          </div>
                          <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700">
                            <div className="text-sm text-slate-400 mb-2">总行驶里程 (km)</div>
                            <div className="text-4xl font-mono text-amber-400 font-bold">456.2</div>
                          </div>
                        </div>
                      </div>
                    )}

                    {currentView === 'settings' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-r border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md p-6">
                        <div className="flex items-center gap-2 mb-6 border-b border-slate-700 pb-4">
                          <Settings className="w-6 h-6 text-cyan-400" />
                          <h2 className="text-xl font-bold text-slate-100">系统配置</h2>
                        </div>
                        <div className="space-y-4">
                          <div className="bg-slate-800/80 p-4 rounded-xl border border-slate-700 flex justify-between items-center">
                            <span className="text-slate-300">自动避障策略</span>
                            <select className="bg-slate-900 border border-slate-600 text-cyan-300 rounded px-2 py-1 outline-none">
                              <option>严格安全</option>
                              <option>效率优先</option>
                            </select>
                          </div>
                          <div className="bg-slate-800/80 p-4 rounded-xl border border-slate-700 flex justify-between items-center">
                            <span className="text-slate-300">低电量回充阈值</span>
                            <div className="flex items-center gap-2">
                              <input type="range" min="10" max="30" defaultValue="20" className="w-24 accent-cyan-500" />
                              <span className="text-cyan-300 font-mono">20%</span>
                            </div>
                          </div>
                          <div className="bg-slate-800/80 p-4 rounded-xl border border-slate-700 flex justify-between items-center">
                            <span className="text-slate-300">全局语音报警</span>
                            <div className="w-10 h-5 bg-cyan-600 rounded-full relative cursor-pointer">
                              <div className="w-4 h-4 bg-white rounded-full absolute right-0.5 top-0.5"></div>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}

                    {/* ---------------- 中间：大图视野 (始终存在) ---------------- */}
                    <div className="flex-1 relative bg-slate-950 overflow-hidden flex flex-col">
                      {/* 地图控制悬浮窗 */}
                      <div className="absolute top-6 left-6 z-20 flex gap-4">
                        <div className="bg-slate-900/90 backdrop-blur-sm px-6 py-3 rounded-xl border border-slate-700 flex items-center gap-3 shadow-2xl">
                          <Layers className="w-6 h-6 text-cyan-400" />
                          <span className="text-lg font-bold">总装二车间_主地图_V4</span>
                        </div>
                      </div>

                      {/* 缩放控制悬浮窗 */}
                      <div className="absolute top-6 right-6 z-20 flex flex-col gap-2 bg-slate-900/90 backdrop-blur-sm p-2 rounded-xl border border-slate-700 shadow-2xl">
                        <button onClick={() => setMapZoom(z => Math.min(z + 0.2, 3))} className="p-3 hover:bg-slate-700 rounded-lg text-slate-300 transition-colors"><ZoomIn className="w-6 h-6" /></button>
                        <button onClick={resetMap} className="p-3 hover:bg-slate-700 rounded-lg text-slate-300 transition-colors border-y border-slate-700"><Maximize className="w-6 h-6" /></button>
                        <button onClick={() => setMapZoom(z => Math.max(z - 0.2, 0.2))} className="p-3 hover:bg-slate-700 rounded-lg text-slate-300 transition-colors"><ZoomOut className="w-6 h-6" /></button>
                      </div>

                      {/* 物理地图交互容器 */}
                      <div
                        className="w-full h-full cursor-grab active:cursor-grabbing relative"
                        onWheel={handleMapWheel}
                        onMouseDown={handleMapDragStart}
                        onMouseMove={handleMapDrag}
                        onMouseUp={handleMapDragEnd}
                        onMouseLeave={handleMapDragEnd}
                      >
                        {/* 背景固定网格（不随拖拽缩放，模拟雷达底色） */}
                        <div className="absolute inset-0 pointer-events-none opacity-20" style={{ backgroundImage: 'radial-gradient(circle, #38bdf8 1px, transparent 1px)', backgroundSize: '40px 40px' }}></div>

                        {/* 可缩放平移的图层 */}
                        <div
                          className="absolute border-4 border-slate-800/50 bg-slate-900/40"
                          style={{
                            width: mapBounds.w,
                            height: mapBounds.h,
                            left: '50%',
                            top: '50%',
                            transform: `translate(calc(-50% + ${mapPan.x}px), calc(-50% + ${mapPan.y}px)) scale(${mapZoom})`,
                            transformOrigin: 'center'
                          }}
                        >
                          {/* 内部平移层：将坐标原点从左上角移到内容中心，配合 mapBounds.minX/Y 实现完美居中 */}
                          <div className="absolute inset-0" style={{ transform: `translate(${-mapBounds.minX}px, ${-mapBounds.minY}px)` }}>
                            {/* 动态厂区拓扑图层 */}
                            <svg className="absolute inset-0 w-full h-full pointer-events-none opacity-50 overflow-visible">
                              {/* 渲染边 */}
                              {mapData.edges.map((edge, idx) => (
                                <line key={idx} x1={edge.x1} y1={edge.y1} x2={edge.x2} y2={edge.y2} stroke="#3b82f6" strokeWidth="4" strokeDasharray="10,10" />
                              ))}
                              {/* 渲染节点 */}
                              {mapData.nodes.map((node, idx) => (
                                <g key={idx}>
                                  <circle cx={node.x} cy={node.y} r="8" fill="#1e293b" stroke="#0ea5e9" strokeWidth="2" />
                                  {node.label && (
                                    <text x={node.x} y={node.y - 15} fill="#bae6fd" fontSize="14" textAnchor="middle" fontWeight="bold">
                                      {node.label}
                                    </text>
                                  )}
                                </g>
                              ))}
                            </svg>

                            {/* 渲染所有 AGV */}
                            {agvs.map(agv => {
                              const isSelected = agv.id === selectedAgvId;
                              const hasError = agv.status === '异常报警';
                              return (
                                <div
                                  key={agv.id}
                                  onClick={(e) => { e.stopPropagation(); setSelectedAgvId(agv.id); }}
                                  className={`absolute flex flex-col items-center justify-center transition-all duration-[100ms] ease-linear cursor-pointer group`}
                                  style={{
                                    left: agv.x,
                                    top: agv.y,
                                    transform: `translate(-50%, -50%)`,
                                    zIndex: isSelected ? 50 : 10
                                  }}
                                >
                                  {/* 选中效果 */}
                                  {isSelected && (
                                    <div className="absolute w-32 h-32 bg-cyan-400/20 rounded-full animate-ping pointer-events-none"></div>
                                  )}

                                  <div
                                    className={`w-12 h-12 rounded flex items-center justify-center relative shadow-2xl border-4 ${isSelected ? 'border-cyan-300 bg-cyan-600 shadow-[0_0_30px_rgba(34,211,238,0.8)] scale-125 z-50' :
                                        hasError ? 'border-rose-400 bg-rose-700 shadow-[0_0_15px_rgba(244,63,94,0.6)]' :
                                          'border-slate-500 bg-slate-700 hover:border-cyan-500 hover:scale-110'
                                      } transition-transform`}
                                    style={{ transform: agv.angle ? `rotate(${agv.angle}rad)` : 'none' }}
                                  >
                                    <div className="absolute top-0 w-0 h-0 border-l-[8px] border-r-[8px] border-b-[12px] border-l-transparent border-r-transparent border-b-white transform -translate-y-1/2"></div>
                                    <Truck className={`w-6 h-6 ${isSelected ? 'text-white' : 'text-slate-400'}`} />
                                  </div>

                                  <div className={`mt-2 px-2 py-1 bg-slate-900/90 rounded text-xs font-mono font-bold whitespace-nowrap border ${isSelected ? 'border-cyan-400 text-cyan-300 scale-125' : hasError ? 'border-rose-500 text-rose-300' : 'border-slate-700 text-slate-400'}`}>
                                    {agv.id}
                                  </div>
                                </div>
                              );
                            })}
                          </div>
                        </div>
                      </div>
                    </div>

                    {/* ---------------- 右侧面板 (动态内容) ---------------- */}
                    {currentView === 'monitoring' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-l border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md">

                        <div className="p-6 flex flex-col h-full gap-6">
                          {/* 区块 3: 快捷控制 (操作员首选) */}
                          <div>
                            <div className="flex items-center gap-2 mb-4">
                              <Crosshair className="w-6 h-6 text-rose-400" />
                              <h2 className="text-xl font-bold text-slate-100">车辆快捷控制</h2>
                            </div>

                            <div className="grid grid-cols-2 gap-4">
                              <button onClick={() => handleControl('继续')} className="flex flex-col items-center justify-center gap-3 p-6 bg-slate-800/80 hover:bg-slate-700 border border-slate-600 rounded-2xl transition-all hover:scale-[1.02] shadow-md group">
                                <Play className="w-10 h-10 text-emerald-400 group-hover:text-emerald-300" />
                                <span className="text-lg font-bold">恢复运行</span>
                                <span className="text-xs text-slate-500 font-mono bg-slate-900 px-2 py-1 rounded">0x00005=1</span>
                              </button>
                              <button onClick={() => handleControl('暂停')} className="flex flex-col items-center justify-center gap-3 p-6 bg-slate-800/80 hover:bg-slate-700 border border-slate-600 rounded-2xl transition-all hover:scale-[1.02] shadow-md group">
                                <Pause className="w-10 h-10 text-amber-400 group-hover:text-amber-300" />
                                <span className="text-lg font-bold">暂停任务</span>
                                <span className="text-xs text-slate-500 font-mono bg-slate-900 px-2 py-1 rounded">0x00004=1</span>
                              </button>
                              <button onClick={() => handleControl('取消')} className="flex flex-col items-center justify-center gap-3 p-6 bg-slate-800/80 hover:bg-slate-700 border border-slate-600 rounded-2xl transition-all hover:scale-[1.02] shadow-md group">
                                <Square className="w-10 h-10 text-rose-400 group-hover:text-rose-300" />
                                <span className="text-lg font-bold">终止/取消</span>
                                <span className="text-xs text-slate-500 font-mono bg-slate-900 px-2 py-1 rounded">0x00006=1</span>
                              </button>
                              <button onClick={() => handleControl('清除报错')} className="flex flex-col items-center justify-center gap-3 p-6 bg-slate-800/80 hover:bg-slate-700 border border-slate-600 rounded-2xl transition-all hover:scale-[1.02] shadow-md group">
                                <RotateCcw className="w-10 h-10 text-indigo-400 group-hover:text-indigo-300" />
                                <span className="text-lg font-bold">复位清错</span>
                                <span className="text-xs text-slate-500 font-mono bg-slate-900 px-2 py-1 rounded">4x00090=1</span>
                              </button>
                            </div>

                            <button className="w-full mt-4 flex items-center justify-center gap-3 p-4 bg-rose-900/60 hover:bg-rose-600 border border-rose-500/80 rounded-xl text-white transition-colors shadow-lg shadow-rose-900/50">
                              <AlertTriangle className="w-7 h-7 animate-pulse" />
                              <span className="text-xl font-black tracking-[0.2em]">全局软件急停</span>
                            </button>
                          </div>

                          <div className="w-full h-px bg-slate-800 my-2"></div>

                          {/* 区块 4: Modbus TCP 测试调试口 */}
                          <div className="flex flex-col flex-1 overflow-hidden">
                            <div className="flex items-center justify-between mb-4">
                              <div className="flex items-center gap-2">
                                <Terminal className="w-6 h-6 text-amber-500" />
                                <h2 className="text-xl font-bold text-slate-100">协议调试口</h2>
                              </div>
                              <span className="text-xs bg-slate-800 text-slate-400 px-2 py-1 rounded border border-slate-700">Port: 502</span>
                            </div>

                            <div className="bg-slate-950 border border-slate-700 rounded-xl p-4 flex flex-col gap-4 shadow-inner">
                              <div className="flex gap-3">
                                <select
                                  className="bg-slate-800 border border-slate-600 text-lg rounded-lg px-3 py-2 focus:outline-none focus:border-cyan-500 text-slate-200"
                                  value={debugCmd.type}
                                  onChange={e => setDebugCmd({ ...debugCmd, type: e.target.value })}
                                >
                                  <option value="0x">0x 线圈</option>
                                  <option value="1x">1x 离散量</option>
                                  <option value="3x">3x 输入寄</option>
                                  <option value="4x">4x 保持寄</option>
                                </select>
                                <input
                                  type="text"
                                  placeholder="地址(如00004)"
                                  className="flex-1 bg-slate-800 border border-slate-600 text-lg rounded-lg px-3 py-2 focus:outline-none focus:border-cyan-500 font-mono placeholder-slate-600 text-slate-200"
                                  value={debugCmd.address}
                                  onChange={e => setDebugCmd({ ...debugCmd, address: e.target.value })}
                                />
                              </div>

                              <div className="flex gap-3">
                                <input
                                  type="text"
                                  placeholder="写入值(HEX/DEC)"
                                  className="flex-1 bg-slate-800 border border-slate-600 text-lg rounded-lg px-3 py-2 focus:outline-none focus:border-cyan-500 font-mono placeholder-slate-600 text-slate-200"
                                  value={debugCmd.value}
                                  onChange={e => setDebugCmd({ ...debugCmd, value: e.target.value })}
                                />
                                <button
                                  onClick={handleDebugSend}
                                  className="bg-cyan-600 hover:bg-cyan-500 text-white px-6 py-2 rounded-lg flex items-center justify-center gap-2 transition-colors font-bold text-lg shadow-lg"
                                >
                                  <Send className="w-5 h-5" /> 写入
                                </button>
                              </div>
                            </div>

                            {/* 通信日志终端 */}
                            <div className="flex-1 mt-4 bg-black border border-slate-700 rounded-xl p-3 flex flex-col font-mono text-sm overflow-hidden shadow-inner relative">
                              <div className="absolute top-0 w-full h-8 bg-gradient-to-b from-black to-transparent z-10 pointer-events-none"></div>
                              <div className="flex-1 overflow-y-auto custom-scrollbar space-y-2 pr-2 mt-2">
                                {selectedAgv.logs.map((log, idx) => (
                                  <div key={idx} className="break-all border-b border-slate-900 pb-1">
                                    <span className="text-slate-500 mr-2">[{log.time}]</span>
                                    <span className={log.msg.includes('TX') ? 'text-cyan-400' : 'text-emerald-400'}>{log.msg}</span>
                                  </div>
                                ))}
                              </div>
                            </div>

                          </div>
                        </div>
                      </div>
                    )}

                    {currentView === 'tasks' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-l border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md p-6">
                        <div className="flex items-center gap-2 mb-6 border-b border-slate-700 pb-4">
                          <Crosshair className="w-6 h-6 text-rose-400" />
                          <h2 className="text-xl font-bold text-slate-100">任务详情与控制</h2>
                        </div>
                        <div className="bg-slate-800/80 p-5 rounded-xl border border-slate-700 mb-6">
                          {tasks.find(t => t.id === selectedTaskId) ? (
                            <>
                              <h3 className="text-lg font-bold text-cyan-300 mb-4">{selectedTaskId}</h3>
                              <div className="space-y-3 text-sm">
                                <div className="flex justify-between"><span className="text-slate-400">承运车辆</span><span className="text-slate-100 font-mono">{tasks.find(t => t.id === selectedTaskId)?.robotId}</span></div>
                                <div className="flex justify-between"><span className="text-slate-400">当前动作</span><span className="text-slate-100">{tasks.find(t => t.id === selectedTaskId)?.stage || '无'}</span></div>
                                <div className="flex justify-between"><span className="text-slate-400">状态</span><span className="text-emerald-400 font-bold">{tasks.find(t => t.id === selectedTaskId)?.statusText}</span></div>
                                <div className="flex justify-between"><span className="text-slate-400">完成情况</span><span className="text-slate-100 font-mono">{tasks.find(t => t.id === selectedTaskId)?.isCompleted ? '是' : '否'}</span></div>
                              </div>
                            </>
                          ) : (
                            <div className="text-slate-500 text-center py-6">请选择一个任务</div>
                          )}
                        </div>
                        <div className="grid grid-cols-2 gap-4">
                          <button className="p-4 bg-rose-900/60 hover:bg-rose-600 border border-rose-500/80 rounded-xl text-white transition-colors font-bold">
                            强制取消任务
                          </button>
                          <button className="p-4 bg-amber-900/60 hover:bg-amber-600 border border-amber-500/80 rounded-xl text-white transition-colors font-bold">
                            挂起/暂停
                          </button>
                          <button className="p-4 bg-cyan-900/60 hover:bg-cyan-600 border border-cyan-500/80 rounded-xl text-white transition-colors font-bold col-span-2">
                            重新分配车辆
                          </button>
                        </div>
                      </div>
                    )}

                    {currentView === 'data' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-l border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md p-6">
                        <div className="flex items-center gap-2 mb-6 border-b border-slate-700 pb-4">
                          <Activity className="w-6 h-6 text-emerald-400" />
                          <h2 className="text-xl font-bold text-slate-100">实时效能分析</h2>
                        </div>
                        <div className="flex-1 flex flex-col gap-4">
                          <div className="bg-slate-800/80 p-4 rounded-xl border border-slate-700 flex-1 flex flex-col">
                            <h3 className="text-sm text-slate-400 mb-4">各区域拥堵指数</h3>
                            <div className="flex-1 flex items-end gap-2">
                              {[40, 70, 20, 90, 50, 30, 60].map((h, i) => (
                                <div key={i} className="flex-1 bg-slate-900 rounded-t-sm relative group">
                                  <div className={`absolute bottom-0 w-full rounded-t-sm transition-all duration-1000 ${h > 80 ? 'bg-rose-500' : h > 50 ? 'bg-amber-400' : 'bg-emerald-400'}`} style={{ height: `${h}%` }}></div>
                                </div>
                              ))}
                            </div>
                            <div className="flex justify-between text-xs text-slate-500 mt-2">
                              <span>A区</span><span>B区</span><span>C区</span><span>D区</span><span>E区</span><span>F区</span><span>G区</span>
                            </div>
                          </div>
                          <div className="bg-slate-800/80 p-4 rounded-xl border border-slate-700">
                            <h3 className="text-sm text-slate-400 mb-2">系统健康度</h3>
                            <div className="flex items-center gap-4">
                              <div className="w-16 h-16 rounded-full border-4 border-emerald-500 flex items-center justify-center text-xl font-bold text-emerald-400">98</div>
                              <div className="text-sm text-slate-300">系统运行平稳，无严重阻塞，通信延迟极低。</div>
                            </div>
                          </div>
                        </div>
                      </div>
                    )}

                    {currentView === 'settings' && (
                      <div className="w-[440px] flex flex-col bg-slate-900/80 border-l border-slate-800 shadow-xl shrink-0 z-10 backdrop-blur-md p-6">
                        <div className="flex items-center gap-2 mb-6 border-b border-slate-700 pb-4">
                          <Users className="w-6 h-6 text-indigo-400" />
                          <h2 className="text-xl font-bold text-slate-100">权限与版本</h2>
                        </div>
                        <div className="space-y-4">
                          <div className="bg-slate-800/80 p-4 rounded-xl border border-slate-700">
                            <div className="text-sm text-slate-400 mb-1">当前登录账号</div>
                            <div className="text-lg font-bold text-slate-200">Admin_Super (超级管理员)</div>
                          </div>
                          <div className="bg-slate-800/80 p-4 rounded-xl border border-slate-700">
                            <div className="text-sm text-slate-400 mb-1">系统版本</div>
                            <div className="text-lg font-mono text-slate-200">v3.2.0-release-20231025</div>
                          </div>
                          <button className="w-full mt-8 p-4 bg-slate-800 hover:bg-slate-700 border border-slate-600 rounded-xl text-slate-200 transition-colors font-bold flex items-center justify-center gap-2">
                            <Unlock className="w-5 h-5" /> 退出登录
                          </button>
                        </div>
                      </div>
                    )}
                  </>
                )}

              </div>
            </div>
            {/* ===================== 软件主界面结束 ===================== */}

          </div>
        </div>

        {/* 显示器底座与支架 (纯 CSS 绘制) */}
        <div className="w-[120px] h-[60px] bg-gradient-to-b from-gray-800 to-gray-900 border-x-4 border-black relative z-0"></div>
        <div className="w-[400px] h-[20px] bg-gray-700 rounded-t-3xl shadow-[0_10px_20px_rgba(0,0,0,0.5)] border-t-2 border-gray-600"></div>
        <div className="w-[400px] h-[8px] bg-gray-900 rounded-b-xl"></div>
      </div>
    </div>
  );
}

