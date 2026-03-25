using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BasicRegionNavigation.ViewModels
{
    /// <summary>
    /// 用于 UI 绑定的地图边模型：支持双向线与带箭头的单向线
    /// </summary>
    public class MapEdgeViewModel : ObservableObject
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public bool IsOneWay { get; set; }

        public double CenterX => (X1 + X2) / 2;
        public double CenterY => (Y1 + Y2) / 2;

        /// <summary>
        /// 线段从起点指向终点的旋转角度（用于箭头转向）
        /// </summary>
        public double Angle
        {
            get
            {
                double radians = Math.Atan2(Y2 - Y1, X2 - X1);
                return radians * (180 / Math.PI);
            }
        }

        public MapEdgeViewModel(double x1, double y1, double x2, double y2, bool isOneWay)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            IsOneWay = isOneWay;
        }
    }
}
