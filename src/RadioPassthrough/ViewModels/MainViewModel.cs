using System.Diagnostics;
using System.Windows.Threading;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly DispatcherTimer _frame;
    private readonly DispatcherTimer _slow;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private int _selectedTab;
    private string _statusText = "Starting…";
    private CheckLevel _statusLevel = CheckLevel.Info;

    public MainViewModel(AppController app)
    {
        _app = app;
        Live = new LiveViewModel(app);
        Test = new TestViewModel(app);
        Setup = new SetupViewModel(app);
        Setup.Refreshed += UpdateStatus;
        _app.Engine.StatusChanged += _ => System.Windows.Application.Current.Dispatcher.BeginInvoke(UpdateStatus);

        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _frame.Tick += (_, _) => Frame();
        _slow = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _slow.Tick += async (_, _) => await Setup.RefreshAsync();
    }

    public LiveViewModel Live { get; }
    public TestViewModel Test { get; }
    public SetupViewModel Setup { get; }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!Set(ref _selectedTab, value)) return;
            OnPropertyChanged(nameof(IsLive));
            OnPropertyChanged(nameof(IsTest));
            OnPropertyChanged(nameof(IsSetup));
            if (value != 0) Live.CancelCapture();
            if (value == 1) _ = Test.RefreshSourcesAsync();
            if (value == 2) _ = Setup.RefreshAsync();
        }
    }

    public bool IsLive => _selectedTab == 0;
    public bool IsTest => _selectedTab == 1;
    public bool IsSetup => _selectedTab == 2;

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public CheckLevel StatusLevel { get => _statusLevel; private set => Set(ref _statusLevel, value); }

    // Timers only run while the window is visible (see SetVisible).
    public void Start()
    {
        _ = Setup.RefreshAsync();
        _ = Setup.CheckForUpdateAsync();
    }

    // Stops UI-only work while the window is hidden in the tray.
    public void SetVisible(bool visible)
    {
        if (visible)
        {
            _frame.Start();
            _slow.Start();
            _ = Setup.RefreshAsync();
        }
        else
        {
            _frame.Stop();
            _slow.Stop();
        }
    }

    private void Frame()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = _lastFrame == 0 ? 0.033 : now - _lastFrame;
        _lastFrame = now;
        Live.Tick(now, dt);
        Test.Tick();
    }

    private void UpdateStatus()
    {
        var status = _app.Engine.Status;
        var blocking = Setup.LatestChecks.FirstOrDefault(c => c.Level == CheckLevel.Blocking);
        var attention = Setup.LatestChecks.FirstOrDefault(c => c.Level == CheckLevel.Attention);

        if (status.Problem is { } problem)
        {
            StatusText = problem;
            StatusLevel = CheckLevel.Blocking;
        }
        else if (blocking is not null)
        {
            StatusText = $"Needs setup  ·  {blocking.Title}";
            StatusLevel = CheckLevel.Blocking;
        }
        else if (status.GameIsTestSource)
        {
            StatusText = $"Testing with {status.GameName}";
            StatusLevel = CheckLevel.Info;
        }
        else if (status.GameAttached)
        {
            StatusText = attention is null ? "Live  ·  Arma 3 attached" : $"Live  ·  check {attention.Title}";
            StatusLevel = attention is null ? CheckLevel.Ok : CheckLevel.Attention;
        }
        else
        {
            StatusText = attention is null ? "Ready  ·  waiting for Arma 3" : $"Ready  ·  check {attention.Title}";
            StatusLevel = attention is null ? CheckLevel.Ok : CheckLevel.Attention;
        }
    }
}
