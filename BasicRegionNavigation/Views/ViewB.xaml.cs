using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core;

namespace BasicRegionNavigation.Views
{
    /// <summary>
    /// Interaction logic for ViewB
    /// </summary>
    public partial class ViewB : UserControl
    {

        private System.Windows.Threading.DispatcherTimer _timer;

        public ViewB()
        {
            InitializeComponent();
            cartesianChart1.LegendTextPaint= new SolidColorPaint(SKColors.White);
            
            _timer = new System.Windows.Threading.DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += _timer_Tick;
            _timer.Start();
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
                        ReactAppWebView.CoreWebView2.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(mapPayload));
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
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                try 
                {
                    ReactAppWebView.CoreWebView2?.PostWebMessageAsJson(json);
                }
                catch { }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
        }
    }
}
