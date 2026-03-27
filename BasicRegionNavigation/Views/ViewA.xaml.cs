using BasicRegionNavigation.ViewModels;
using Core;
using HandyControl.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace BasicRegionNavigation.Views
{
    /// <summary>
    /// Interaction logic for ViewA
    /// </summary>
    public partial class ViewA : UserControl
    {

        private int Modules = Global.Modules;
        public ViewA()
        {
            InitializeComponent();
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

        private void EditText_Click(object sender, RoutedEventArgs e)
        {
        }
        private void ClockRadioButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            //右键执行一个vm中的命令
            var vm = (ViewAViewModel)this.DataContext;
            if (sender is ClockRadioButton radioBtn)
            {
                var parameter = radioBtn.CommandParameter;
                vm.ShowTextCommand.Execute(parameter);

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
