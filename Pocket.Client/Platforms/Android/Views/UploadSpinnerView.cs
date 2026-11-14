using Android.Content;
using Android.Graphics;
using Android.Views;
using Paint = Android.Graphics.Paint;
using Color = Android.Graphics.Color;
using Canvas = Android.Graphics.Canvas;
using RectF = Android.Graphics.RectF;

namespace Pocket.Client.Platforms.Android.Views
{
    public class UploadSpinnerView : global::Android.Views.View
    {
        private readonly Paint _arcPaint;
        private readonly RectF _arcBounds = new();
        private readonly float _density;
        private bool _isRunning;
        private long _startTimeMs;

        public UploadSpinnerView(Context context) : base(context)
        {
            _density = context.Resources?.DisplayMetrics?.Density ?? 2.75f;

            _arcPaint = new Paint(PaintFlags.AntiAlias)
            {
                Color = Color.White,
                StrokeWidth = 2.2f * _density,
                StrokeCap = Paint.Cap.Round!
            };
            _arcPaint.SetStyle(Paint.Style.Stroke);

            _startTimeMs = global::Android.OS.SystemClock.UptimeMillis();
        }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning == value)
                    return;
                _isRunning = value;
                if (_isRunning)
                {
                    _startTimeMs = global::Android.OS.SystemClock.UptimeMillis();
                }
                PostInvalidateOnAnimation();
            }
        }

        public void SetArcColor(Color color)
        {
            _arcPaint.Color = color;
            Invalidate();
        }

        public void SetStrokeThicknessDp(float dp)
        {
            _arcPaint.StrokeWidth = System.Math.Max(1f, dp * _density);
            Invalidate();
        }

        protected override void OnAttachedToWindow()
        {
            base.OnAttachedToWindow();
            if (_isRunning)
            {
                _startTimeMs = global::Android.OS.SystemClock.UptimeMillis();
                PostInvalidateOnAnimation();
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);

            if (!_isRunning || Width <= 0 || Height <= 0)
                return;

            float stroke = _arcPaint.StrokeWidth;
            float size = System.Math.Min(Width, Height);
            float radius = System.Math.Max(1f, (size - stroke) * 0.5f - (0.5f * _density));
            float cx = Width * 0.5f;
            float cy = Height * 0.5f;

            _arcBounds.Set(cx - radius, cy - radius, cx + radius, cy + radius);

            long elapsedMs = global::Android.OS.SystemClock.UptimeMillis() - _startTimeMs;
            float startAngle = ((elapsedMs % 900L) / 900f) * 360f;
            const float sweepAngle = 105f;

            canvas.DrawArc(_arcBounds, startAngle, sweepAngle, false, _arcPaint);

            if (IsAttachedToWindow && WindowVisibility == ViewStates.Visible)
            {
                PostInvalidateOnAnimation();
            }
        }
    }
}
