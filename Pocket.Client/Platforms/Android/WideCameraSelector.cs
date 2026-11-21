using Android.Content;
using Android.Hardware.Camera2;

namespace Pocket.Client.Platforms.Android
{
    /// <summary>
    /// Picks the widest camera of a facing. Some phones expose the same lens more than once with different
    /// sensor crops; e.g. the Galaxy Note 9 front camera is id 1 (2640x1980 crop, ~58° horizontal) and
    /// id 2 (full 3264x2448 sensor, ~69°, Samsung's "wide selfie"). Apps that open the first front camera get
    /// the narrow one. Camera1 and Camera2 ids match on these devices (Camera1 is a shim over Camera2).
    /// </summary>
    internal static class WideCameraSelector
    {
        private const int CapabilityBackwardCompatible = 0;

        /// <summary>Camera2 id of the widest normal (backward-compatible) camera facing front/back, or null.</summary>
        public static string? WidestCameraId(bool front)
        {
            try
            {
                var manager = (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService)!;
                string? best = null;
                double bestFov = 0;
                foreach (var id in manager.GetCameraIdList())
                {
                    var c = manager.GetCameraCharacteristics(id);
                    int facing = (c.Get(CameraCharacteristics.LensFacing) as Java.Lang.Integer)?.IntValue() ?? -1;
                    if (facing != (front ? 0 : 1)) continue;
                    if (!IsBackwardCompatible(c)) continue; // skip depth / IR-only cameras

                    double fov = HorizontalFov(c);
                    // Strictly wider (by >1°) to keep the system's first camera when they are equivalent
                    if (best == null || fov > bestFov + 1)
                    {
                        best = id;
                        bestFov = fov;
                    }
                }
                return best;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Widest camera lookup error: {ex.Message}");
                return null;
            }
        }

        private static bool IsBackwardCompatible(CameraCharacteristics c)
        {
            var caps = c.Get(CameraCharacteristics.RequestAvailableCapabilities);
            if (caps == null || !caps.Class.IsArray) return true;
            int n = Java.Lang.Reflect.Array.GetLength(caps);
            for (int i = 0; i < n; i++)
                if (Java.Lang.Reflect.Array.GetInt(caps, i) == CapabilityBackwardCompatible) return true;
            return false;
        }

        /// <summary>Horizontal field of view (degrees, sensor long side) of the active array at the shortest focal length.</summary>
        private static double HorizontalFov(CameraCharacteristics c)
        {
            var active = c.Get(CameraCharacteristics.SensorInfoActiveArraySize) as global::Android.Graphics.Rect;
            var pixel = c.Get(CameraCharacteristics.SensorInfoPixelArraySize) as global::Android.Util.Size;
            var physical = c.Get(CameraCharacteristics.SensorInfoPhysicalSize) as global::Android.Util.SizeF;
            var focals = c.Get(CameraCharacteristics.LensInfoAvailableFocalLengths);
            if (active == null || pixel == null || physical == null || focals == null || !focals.Class.IsArray ||
                pixel.Width <= 0 || Java.Lang.Reflect.Array.GetLength(focals) == 0)
                return 0;

            float focal = float.MaxValue;
            for (int i = 0; i < Java.Lang.Reflect.Array.GetLength(focals); i++)
                focal = System.Math.Min(focal, Java.Lang.Reflect.Array.GetFloat(focals, i));
            if (focal <= 0) return 0;

            double width = physical.Width * active.Width() / pixel.Width;
            return 2 * System.Math.Atan(width / (2 * focal)) * 180 / System.Math.PI;
        }
    }
}
