using System.Threading.Tasks; // 补全引用
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BasicRegionNavigation.Controls
{
    // 定义设备状态枚举
    public enum DeviceState
    {
        Offline, // 离线/停止
        Running, // 运行
        Alarm    // 报警
    }

    public partial class StatusBox : UserControl
    {
        public StatusBox()
        {
            InitializeComponent();

            // 构造函数中不再需要手动调用 UpdateVisuals，1
            // 因为依赖属性初始化时会自动触发回调（如果值与默认值不同），
            // 或者我们可以手动触发一次当前颜色的文字更新以防万一。
            UpdateTextFromColor(StatusColor);

              
        }

        #region 静态资源 (颜色定义)

        private static readonly Brush ColorOk = CreateFrozenBrush(Colors.LimeGreen);
        private static readonly Brush ColorNg = CreateFrozenBrush(Colors.Red);
        private static readonly Brush ColorOffline = CreateFrozenBrush(Colors.Gray);

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        #endregion

        #region 依赖属性

        // 1. Title (不变)
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(StatusBox), new PropertyMetadata("设备名称"));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        // 2. Output (不变)
        public static readonly DependencyProperty OutputProperty =
            DependencyProperty.Register("Output", typeof(string), typeof(StatusBox), new PropertyMetadata("0"));

        public string Output
        {
            get { return (string)GetValue(OutputProperty); }
            set { SetValue(OutputProperty, value); }
        }

        // 3. StatusColor (修改：添加 OnStatusColorChanged 回调)
        public static readonly DependencyProperty StatusColorProperty =
            DependencyProperty.Register("StatusColor", typeof(Brush), typeof(StatusBox),
                new PropertyMetadata(ColorOffline, OnStatusColorChanged)); // <--- 关键修改

        public Brush StatusColor
        {
            get { return (Brush)GetValue(StatusColorProperty); }
            set { SetValue(StatusColorProperty, value); }
        }

        // 4. StatusText (不变)
        public static readonly DependencyProperty StatusTextProperty =
            DependencyProperty.Register("StatusText", typeof(string), typeof(StatusBox), new PropertyMetadata("停止"));

        public string StatusText
        {
            get { return (string)GetValue(StatusTextProperty); }
            set { SetValue(StatusTextProperty, value); }
        }

        #endregion

        #region 核心逻辑：颜色驱动文字

        // 当 StatusColor 发生变化时触发
        private static void OnStatusColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StatusBox control && e.NewValue is Brush newBrush)
            {
                control.UpdateTextFromColor(newBrush);
            }
        }

        // 根据颜色判断文字
        private void UpdateTextFromColor(Brush brush)
        {
            if (brush is SolidColorBrush solidBrush)
            {
                Color c = solidBrush.Color;

                // 比较颜色值 (Color 结构体比较是值比较，很安全)
                if (c == Colors.LimeGreen)
                {
                    StatusText = "运行";
                }
                else if (c == Colors.Red)
                {
                    StatusText = "报警";
                }
                else if (c == Colors.Gray)
                {
                    StatusText = "停止";
                }
                else
                {
                    // 如果传入了未知的颜色 (比如蓝色)，可以显示默认值或"未知"
                    StatusText = "未知";
                }
            }
        }

        #endregion

        #region 辅助逻辑：枚举驱动颜色

        // 5. CurrentState 
        // 现在的逻辑链条是：CurrentState 变化 -> 修改 StatusColor -> 触发 OnStatusColorChanged -> 修改 StatusText
        public static readonly DependencyProperty CurrentStateProperty =
            DependencyProperty.Register("CurrentState", typeof(DeviceState), typeof(StatusBox),
                new PropertyMetadata(DeviceState.Offline, OnStateChanged));

        public DeviceState CurrentState
        {
            get { return (DeviceState)GetValue(CurrentStateProperty); }
            set { SetValue(CurrentStateProperty, value); }
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StatusBox control && e.NewValue is DeviceState newState)
            {
                // 这里只负责改颜色
                switch (newState)
                {
                    case DeviceState.Running:
                        control.StatusColor = ColorOk;
                        break;
                    case DeviceState.Alarm:
                        control.StatusColor = ColorNg;
                        break;
                    case DeviceState.Offline:
                    default:
                        control.StatusColor = ColorOffline;
                        break;
                }
            }
        }

        #endregion
    }
}