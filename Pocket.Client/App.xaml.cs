using Pocket.Client.PageModels;

namespace Pocket.Client
{
    public partial class App : Application
    {
        private readonly AppShell _appShell;

        public App(AppShell appShell)
        {
            InitializeComponent();
            _appShell = appShell;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(_appShell);
            window.Created += (_, _) => SetForegroundState(true);
            window.Activated += (_, _) => SetForegroundState(true);
            window.Resumed += (_, _) => SetForegroundState(true);
            window.Deactivated += (_, _) => SetForegroundState(false);
            window.Stopped += (_, _) => SetForegroundState(false);
            return window;
        }

        private void SetForegroundState(bool isForeground)
        {
            if (_appShell.BindingContext is AppShellModel shellModel)
            {
                shellModel.IsAppInForeground = isForeground;
            }
        }
    }
}