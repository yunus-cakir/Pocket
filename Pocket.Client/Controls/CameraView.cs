using Microsoft.Maui.Controls;

namespace Pocket.Client.Controls
{
    public class CameraView : View
    {
        public event EventHandler<string>? PhotoCaptured;

        public void CapturePhoto()
        {
            Handler?.Invoke(nameof(CapturePhoto));
        }

        public void OnPhotoCaptured(string filePath)
        {
            PhotoCaptured?.Invoke(this, filePath);
        }
    }
}
