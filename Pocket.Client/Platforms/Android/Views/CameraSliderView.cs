using Android.Content;
using Android.Graphics;
using Android.Views;
using Paint = Android.Graphics.Paint;
using Color = Android.Graphics.Color;
using Canvas = Android.Graphics.Canvas;

namespace Pocket.Client.Platforms.Android.Views
{
    /// <summary>
    /// Native drawing/touch view for <see cref="Pocket.Client.Controls.CameraSlider"/>.
    /// Calls RequestDisallowInterceptTouchEvent(true) on ACTION_DOWN so ancestors
    /// (SwipeInterceptLayout, RecyclerView) do not intercept the drag.
    /// </summary>
    public class CameraSliderView : global::Android.Views.View
    {
        private readonly Paint _trackPaint;
        private readonly Paint _thumbPaint;
        private readonly Paint _thumbStrokePaint;
        private readonly Paint _tickPaint;
        private readonly float _density;

        public float Minimum { get; set; }
        public float Maximum { get; set; } = 1f;
        public float Value { get; set; }
        public bool IsVertical { get; set; }
        public bool ShowCenterTick { get; set; }

        public Action<float>? ValueChangedByUser { get; set; }
        public Action? DragStarted { get; set; }
        public Action? DragCompleted { get; set; }

        private float ThumbRadius => 10f * _density;
        private float Pad => ThumbRadius + 3f * _density;

        public CameraSliderView(Context context) : base(context)
        {
            _density = context.Resources?.DisplayMetrics?.Density ?? 2f;

            _trackPaint = new Paint(PaintFlags.AntiAlias) { Color = Color.Argb(150, 255, 255, 255), StrokeWidth = 3f * _density, StrokeCap = Paint.Cap.Round! };
            _trackPaint.SetStyle(Paint.Style.Stroke);

            _tickPaint = new Paint(PaintFlags.AntiAlias) { Color = Color.Argb(220, 255, 255, 255), StrokeWidth = 2f * _density, StrokeCap = Paint.Cap.Round! };
            _tickPaint.SetStyle(Paint.Style.Stroke);

            _thumbPaint = new Paint(PaintFlags.AntiAlias) { Color = Color.Rgb(0xF5, 0xB3, 0x01) };
            _thumbPaint.SetStyle(Paint.Style.Fill);

            _thumbStrokePaint = new Paint(PaintFlags.AntiAlias) { Color = Color.White, StrokeWidth = 2f * _density };
            _thumbStrokePaint.SetStyle(Paint.Style.Stroke);
            _thumbStrokePaint.SetShadowLayer(4f * _density, 0, 0, Color.Argb(110, 0, 0, 0));
            SetLayerType(LayerType.Software, _thumbStrokePaint); // required for shadow layer
        }

        private float Fraction(float value)
        {
            float range = Maximum - Minimum;
            if (range <= 0) return 0f;
            return System.Math.Clamp((value - Minimum) / range, 0f, 1f);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            float w = Width, h = Height, pad = Pad;

            if (IsVertical)
            {
                float cx = w / 2f;
                canvas.DrawLine(cx, pad, cx, h - pad, _trackPaint);

                if (ShowCenterTick && Minimum < 0 && Maximum > 0)
                {
                    float ty = h - pad - Fraction(0) * (h - 2 * pad);
                    canvas.DrawLine(cx - 6 * _density, ty, cx + 6 * _density, ty, _tickPaint);
                }

                // Maximum at the top
                float y = h - pad - Fraction(Value) * (h - 2 * pad);
                canvas.DrawCircle(cx, y, ThumbRadius, _thumbPaint);
                canvas.DrawCircle(cx, y, ThumbRadius, _thumbStrokePaint);
            }
            else
            {
                float cy = h / 2f;
                canvas.DrawLine(pad, cy, w - pad, cy, _trackPaint);

                if (ShowCenterTick && Minimum < 0 && Maximum > 0)
                {
                    float tx = pad + Fraction(0) * (w - 2 * pad);
                    canvas.DrawLine(tx, cy - 6 * _density, tx, cy + 6 * _density, _tickPaint);
                }

                float x = pad + Fraction(Value) * (w - 2 * pad);
                canvas.DrawCircle(x, cy, ThumbRadius, _thumbPaint);
                canvas.DrawCircle(x, cy, ThumbRadius, _thumbStrokePaint);
            }
        }

        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (e == null || !Enabled) return false;

            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    // Claim the gesture before any ancestor decides to intercept.
                    Parent?.RequestDisallowInterceptTouchEvent(true);
                    DragStarted?.Invoke();
                    UpdateFromTouch(e);
                    return true;

                case MotionEventActions.Move:
                    UpdateFromTouch(e);
                    return true;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    UpdateFromTouch(e);
                    Parent?.RequestDisallowInterceptTouchEvent(false);
                    DragCompleted?.Invoke();
                    return true;
            }

            return base.OnTouchEvent(e);
        }

        private void UpdateFromTouch(MotionEvent e)
        {
            float pad = Pad;
            float fraction = IsVertical
                ? 1f - (e.GetY() - pad) / System.Math.Max(1f, Height - 2 * pad)
                : (e.GetX() - pad) / System.Math.Max(1f, Width - 2 * pad);

            fraction = System.Math.Clamp(fraction, 0f, 1f);
            float newValue = Minimum + fraction * (Maximum - Minimum);

            if (System.Math.Abs(newValue - Value) > float.Epsilon)
            {
                Value = newValue;
                Invalidate();
                ValueChangedByUser?.Invoke(newValue);
            }
        }
    }
}
