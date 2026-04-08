using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using System.Windows.Data;
using BasicRegionNavigation.ViewModels;
using Core;
using HandyControl.Controls;

namespace BasicRegionNavigation.Views
{
    /// <summary>
    /// Interaction logic for ViewA
    /// </summary>
    public partial class ViewA : UserControl
    {

        private int Modules = Global.Modules;
        private DispatcherTimer _timer;

        public ViewA()
        {
            InitializeComponent();
            
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500); // 降低频率以减少开销，ViewB是100ms
            _timer.Tick += _timer_Tick;
            _timer.Start();

            ReactAppWebView.WebMessageReceived += ReactAppWebView_WebMessageReceived;
        }

        private void ReactAppWebView_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string jsonStr = e.WebMessageAsJson;
                using (var doc = JsonDocument.Parse(jsonStr))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var typeProp))
                    {
                        string type = typeProp.GetString();
                        if (type == "load_local_config")
                        {
                            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "agv_config.json");
                            if (!File.Exists(configPath))
                            {
                                configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Configs", "agv_config.json");
                            }

                            if (File.Exists(configPath))
                            {
                                string content = File.ReadAllText(configPath);
                                var response = new { type = "map_config_load_response", data = JsonSerializer.Deserialize<object>(content) };
                                ReactAppWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                            }
                        }
                        else if (type == "save_local_config")
                        {
                            if (root.TryGetProperty("data", out var dataProp))
                            {
                                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "agv_config.json");
                                if (!File.Exists(configPath))
                                {
                                    configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Configs", "agv_config.json");
                                }

                                string json = JsonSerializer.Serialize(dataProp, new JsonSerializerOptions { WriteIndented = true });
                                File.WriteAllText(configPath, json);
                                System.Windows.MessageBox.Show("配置已成功保存到本地 agv_config.json", "系统提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 可以加日志
            }
        }

        private void _timer_Tick(object sender, EventArgs e)
        {
            // 每次 tick 都发送地图数据，确保 React 页面在任何时候重载都能收到拓扑
            if (BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalMapNodes != null && BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalMapEdges != null)
            {
                try 
                {
                    if (ReactAppWebView.CoreWebView2 != null)
                    {
                        var mapNodes = BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalMapNodes.Select(n => new { id = n.Id, x = n.X + 100, y = n.Y + 100, label = n.DisplayLabel }).ToList();
                        var mapEdges = BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalMapEdges.Select(edge => new { x1 = edge.X1 + 100, y1 = edge.Y1 + 100, x2 = edge.X2 + 100, y2 = edge.Y2 + 100 }).ToList();
                        var mapPayload = new { type = "map_update", nodes = mapNodes, edges = mapEdges };
                        ReactAppWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(mapPayload));
                    }
                }
                catch { }
            }

            if (BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalRobots != null)
            {
                var list = BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalRobots.ToList();
                var data = list.Select(r => new {
                    id = r.Id,
                    x = r.CurrentX + 100, // +100 for basic offset padding in react map
                    y = r.CurrentY + 100,
                    status = r.CurrentStateText,
                    battery = r.BatteryLevel,
                    isCharging = r.State == BasicRegionNavigation.Common.RobotState.CHARGING,
                    currentTask = string.IsNullOrEmpty(r.CurrentTaskDesc) ? "无任务" : r.CurrentTaskDesc,
                    speed = (r.State == BasicRegionNavigation.Common.RobotState.MOVING) ? 1.0 : 0.0,
                    angle = 0.0, 
                    heldLocks = new string[]{},
                    liftAngle = 0,
                    logs = new object[]{}
                }).ToList();
                
                var payload = new { type = "robot_update", data = data };
                var json = JsonSerializer.Serialize(payload);
                try 
                {
                    ReactAppWebView.CoreWebView2?.PostWebMessageAsJson(json);
                }
                catch { }
            }

            if (BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalActiveTasks != null)
            {
                var tasks = BasicRegionNavigation.ViewModels.ViewAViewModel.GlobalActiveTasks.ToList();
                var taskData = tasks.Select(t => new {
                    id = t.OrderId,
                    statusText = t.StatusDisplayText,
                    stage = t.StageDescription,
                    robotId = string.IsNullOrEmpty(t.AssignedRobotId) || t.AssignedRobotId == "-" ? "未分配" : t.AssignedRobotId,
                    isCompleted = t.IsCompleted
                }).ToList();
                
                var payload = new { type = "task_update", data = taskData };
                var json = JsonSerializer.Serialize(payload);
                try 
                {
                    ReactAppWebView.CoreWebView2?.PostWebMessageAsJson(json);
                }
                catch { }
            }
        }

        private Point _dragStartPoint;
        private bool _isDragging = false;

        private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true; 
            
            double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
            double newScale = Math.Max(0.2, Math.Min(5.0, MapScaleTransform.ScaleX * zoomFactor));

            Point relativePoint = e.GetPosition(MapCanvas);
            
            MapScaleTransform.ScaleX = newScale;
            MapScaleTransform.ScaleY = newScale;
            
            MapScrollViewer.ScrollToHorizontalOffset(relativePoint.X * newScale - e.GetPosition(MapScrollViewer).X);
            MapScrollViewer.ScrollToVerticalOffset(relativePoint.Y * newScale - e.GetPosition(MapScrollViewer).Y);
        }

        private void MapScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
            {
                if (e.Source is Button || e.Source is System.Windows.Controls.TextBox || e.Source is System.Windows.Controls.ComboBox) 
                    return;

                _dragStartPoint = e.GetPosition(MapScrollViewer);
                _isDragging = true;
                MapScrollViewer.CaptureMouse();
                e.Handled = true;
                MapScrollViewer.Cursor = Cursors.Hand;
            }
        }

        private void MapScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(MapScrollViewer);
                Vector diff = _dragStartPoint - currentPoint;
                
                MapScrollViewer.ScrollToHorizontalOffset(MapScrollViewer.HorizontalOffset + diff.X);
                MapScrollViewer.ScrollToVerticalOffset(MapScrollViewer.VerticalOffset + diff.Y);
                
                _dragStartPoint = currentPoint;
            }
        }

        private void MapScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                MapScrollViewer.ReleaseMouseCapture();
                MapScrollViewer.Cursor = Cursors.Arrow;
            }
        }


    }

    public class IntToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 1. 处理 bool 类型 (新增逻辑)
            if (value is bool boolValue)
            {
                // true = 绿色, false = 红色
                return boolValue ? Brushes.Green : Brushes.Red;
            }

            // 2. 处理 short 类型 (原有逻辑)
            if (value is short intValue)
            {
                return intValue switch
                {
                    1 => Brushes.Green,
                    2 => Brushes.Red,
                    _ => Brushes.Gray,
                    //_ => Brushes.Transparent
                };
            }

            // 3. 兼容 int 类型 (防止绑定源是int而不是short导致的转换失败)
            if (value is int regularIntValue)
            {
                return regularIntValue switch
                {
                    1 => Brushes.Green,
                    2 => Brushes.Red,
                    3 => Brushes.Gray,
                    _ => Brushes.Transparent
                };
            }

            // 如果不是 bool 也不是数字，返回默认颜色
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }



}
