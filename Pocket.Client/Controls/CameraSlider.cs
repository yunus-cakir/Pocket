using Microsoft.Maui.Controls;

namespace Pocket.Client.Controls
{
    /// <summary>
    /// Minimal camera-overlay slider (thin track + gold thumb), horizontal or vertical.
    /// The Android implementation claims the touch stream on ACTION_DOWN so that the
    /// horizontal panel swipe and the vertical feed scroll never steal the drag.
    /// </summary>
    public class CameraSlider : View
    {
        public static readonly BindableProperty MinimumProperty =
            BindableProperty.Create(nameof(Minimum), typeof(double), typeof(CameraSlider), 0d);

        public static readonly BindableProperty MaximumProperty =
            BindableProperty.Create(nameof(Maximum), typeof(double), typeof(CameraSlider), 1d);

        public static readonly BindableProperty ValueProperty =
            BindableProperty.Create(nameof(Value), typeof(double), typeof(CameraSlider), 0d, BindingMode.TwoWay,
                propertyChanged: (b, o, n) => ((CameraSlider)b).ValueChanged?.Invoke(b, (double)n));

        public static readonly BindableProperty IsVerticalProperty =
            BindableProperty.Create(nameof(IsVertical), typeof(bool), typeof(CameraSlider), false);

        /// <summary>When true, a small center tick is drawn at value 0 (useful for exposure ±EV).</summary>
        public static readonly BindableProperty ShowCenterTickProperty =
            BindableProperty.Create(nameof(ShowCenterTick), typeof(bool), typeof(CameraSlider), false);

        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool IsVertical { get => (bool)GetValue(IsVerticalProperty); set => SetValue(IsVerticalProperty, value); }
        public bool ShowCenterTick { get => (bool)GetValue(ShowCenterTickProperty); set => SetValue(ShowCenterTickProperty, value); }

        public event EventHandler<double>? ValueChanged;
        public event EventHandler? DragStarted;
        public event EventHandler? DragCompleted;

        public void RaiseDragStarted() => DragStarted?.Invoke(this, EventArgs.Empty);
        public void RaiseDragCompleted() => DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}

