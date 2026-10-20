using SmartBudgetML.Services.Auth;

namespace SmartBudgetML;

public partial class App : Application
{
    private readonly ILocalAuthService? _authService;

    public App(ILocalAuthService? authService = null)
    {
        InitializeComponent();
        _authService = authService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage());

        window.Deactivated += (_, _) => _authService?.RecordBackgrounded();
        window.Stopped += (_, _) => _authService?.RecordBackgrounded();
        window.Resumed += async (_, _) =>
        {
            if (_authService != null)
            {
                await _authService.EvaluateAutoLockOnResumeAsync();
            }
        };

        return window;
    }
}
