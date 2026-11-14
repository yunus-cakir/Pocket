using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Pocket.Client.Controls
{
    /// <summary>
    /// Minimalist circular arc spinner matching the Locket "Uploading..." indicator.
    /// Continuously rotates a smooth white partial-circle arc when <see cref="IsRunning"/> is true.
    /// </summary>
    public class UploadSpinner : View
    {
        public static readonly BindableProperty IsRunningProperty =
            BindableProperty.Create(nameof(IsRunning), typeof(bool), typeof(UploadSpinner), false);

        public static readonly BindableProperty ColorProperty =
            BindableProperty.Create(nameof(Color), typeof(Color), typeof(UploadSpinner), Colors.White);

        public static readonly BindableProperty StrokeThicknessProperty =
            BindableProperty.Create(nameof(StrokeThickness), typeof(double), typeof(UploadSpinner), 2.2d);

        public bool IsRunning
        {
            get => (bool)GetValue(IsRunningProperty);
            set => SetValue(IsRunningProperty, value);
        }

        public Color Color
        {
            get => (Color)GetValue(ColorProperty);
            set => SetValue(ColorProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }
    }
}
