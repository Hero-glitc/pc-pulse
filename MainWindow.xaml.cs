using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PCPulse.Models;
using PCPulse.Services;

namespace PCPulse;

public partial class MainWindow : Window
{
    private readonly HardwareTiers _tiers;
    private readonly Optimizer _optimizer;
    private readonly LiveMonitor _liveMonitor;
    private readonly RequirementsDatabase _db;
    private readonly DispatcherTimer _liveTimer = null!;
    private HardwareScanner _scanner;
    private SystemProfile? _profile;
    private UsageProfile _currentProfile = UsageProfile.Normal;
    private bool _scanning;

    // Sidebar
    private TextBlock _scanStatusText = null!;
    private ProgressBar _scanProgress = null!;
    private Button _btnRefresh = null!;
    private TextBlock _adminBadge = null!;
    private TextBlock _powerNote = null!;
    private Button _btnGaming = null!;
    private Button _btnGraphic = null!;
    private Button _btnNormal = null!;

    // Search
    private Border _searchSection = null!;
    private TextBox _searchTextBox = null!;
    private Button _btnSearch = null!;
    private Border _searchResultCard = null!;
    private TextBlock _searchResultTitle = null!;
    private StackPanel _searchResultDetails = null!;

    // Alerts
    private Border _bottleneckAlert = null!;
    private TextBlock _bottleneckText = null!;

    // Live monitor
    private ProgressBar _liveCpuBar = null!;
    private TextBlock _liveCpuText = null!;
    private ProgressBar _liveRamBar = null!;
    private TextBlock _liveRamText = null!;
    private ProgressBar _liveDiskBar = null!;
    private TextBlock _liveDiskText = null!;

    // Components
    private TextBlock _cpuName = null!;
    private ProgressBar _cpuScore = null!;
    private StackPanel _cpuDetails = null!;
    private TextBlock _cpuBottleneckBadge = null!;
    private Border _cpuCard = null!;
    private TextBlock _gpuName = null!;
    private ProgressBar _gpuScore = null!;
    private StackPanel _gpuDetails = null!;
    private TextBlock _gpuBottleneckBadge = null!;
    private Border _gpuCard = null!;
    private TextBlock _ramName = null!;
    private ProgressBar _ramScore = null!;
    private StackPanel _ramDetails = null!;
    private TextBlock _ramBottleneckBadge = null!;
    private Border _ramCard = null!;
    private TextBlock _storageName = null!;
    private ProgressBar _storageScore = null!;
    private StackPanel _storageDetails = null!;
    private TextBlock _storageBottleneckBadge = null!;
    private Border _storageCard = null!;

    // Upgrade + optimize
    private Border _upgradeCard = null!;
    private TextBlock _upgradeText = null!;
    private Button _btnOptimize = null!;
    private Button _btnRestore = null!;
    private Border _optimizeResult = null!;
    private TextBlock _optimizeResultText = null!;

    // Brushes
    private static readonly Brush BgDark = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e));
    private static readonly Brush BgCard = new SolidColorBrush(Color.FromRgb(0x16, 0x21, 0x3e));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x0f, 0x34, 0x60));
    private static readonly Brush TextPrimary = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0));
    private static readonly Brush TextSecondary = new SolidColorBrush(Color.FromRgb(0xa0, 0xa0, 0xb0));
    private static readonly Brush Success = new SolidColorBrush(Color.FromRgb(0x00, 0xe6, 0x76));
    private static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x40));
    private static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xff, 0x52, 0x52));
    private static readonly Brush GpuColor = new SolidColorBrush(Color.FromRgb(0xe9, 0x45, 0x60));
    private static readonly Brush CpuColor = new SolidColorBrush(Color.FromRgb(0x0e, 0xa5, 0xe9));
    private static readonly Brush RamColor = new SolidColorBrush(Color.FromRgb(0xa7, 0x8b, 0xfa));
    private static readonly Brush StorageColor = new SolidColorBrush(Color.FromRgb(0x34, 0xd3, 0x99));

    public MainWindow()
    {
        Title = "PC Pulse";
        Width = 1150;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = BgDark;
        FontFamily = new FontFamily("Segoe UI");

        _tiers = new HardwareTiers();
        _tiers.Load();
        _optimizer = new Optimizer();
        _liveMonitor = new LiveMonitor();
        _db = new RequirementsDatabase();
        _scanner = new HardwareScanner(_tiers);

        try
        {
            BuildUI();
        }
        catch (Exception ex)
        {
            Content = new TextBlock
            {
                Text = "UI Error: " + ex.Message,
                Foreground = Brushes.Red,
                FontSize = 16,
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap
            };
            return;
        }

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _liveTimer.Tick += LiveTick;

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => _liveTimer.Stop();
    }

    // ==================== UI CONSTRUCTION ====================

    private void BuildUI()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Content = root;

        // ---------- Sidebar ----------
        var sidebar = new StackPanel { Background = BgDark, Margin = new Thickness(16) };
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        sidebar.Children.Add(new TextBlock
        {
            Text = "PC PULSE",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimary,
            Margin = new Thickness(8, 8, 8, 20)
        });

        var scanCard = CreateCard(padding: 12);
        var scanPanel = new StackPanel();
        _scanStatusText = new TextBlock
        {
            Text = "Ready to scan",
            Foreground = TextSecondary,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _scanProgress = new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        _btnRefresh = CreateButton("Refresh", Accent, BtnRefresh_Click);
        _btnRefresh.Margin = new Thickness(0, 10, 0, 0);
        _btnRefresh.Padding = new Thickness(12, 8, 12, 8);
        _btnRefresh.FontSize = 13;
        scanPanel.Children.Add(_scanStatusText);
        scanPanel.Children.Add(_scanProgress);
        scanPanel.Children.Add(_btnRefresh);
        scanCard.Child = scanPanel;
        sidebar.Children.Add(scanCard);

        sidebar.Children.Add(new TextBlock
        {
            Text = "USAGE PROFILE",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = TextSecondary,
            Margin = new Thickness(8, 20, 8, 8)
        });

        _btnGaming = CreateButton("Gaming", GpuColor, BtnGaming_Click);
        _btnGraphic = CreateButton("Graphic Use", RamColor, BtnGraphic_Click);
        _btnNormal = CreateButton("Normal Use", StorageColor, BtnNormal_Click);
        sidebar.Children.Add(_btnGaming);
        sidebar.Children.Add(_btnGraphic);
        sidebar.Children.Add(_btnNormal);

        _adminBadge = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = Warning,
            Margin = new Thickness(8, 20, 8, 0),
            TextAlignment = TextAlignment.Center
        };
        sidebar.Children.Add(_adminBadge);

        _powerNote = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = TextSecondary,
            Margin = new Thickness(8, 12, 8, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        sidebar.Children.Add(_powerNote);

        // ---------- Main area ----------
        var mainGrid = new Grid { Background = BgDark };
        Grid.SetColumn(mainGrid, 1);
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(mainGrid);

        BuildSearchArea(mainGrid);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scrollViewer, 1);
        mainGrid.Children.Add(scrollViewer);

        var dash = new StackPanel { Margin = new Thickness(16) };
        scrollViewer.Content = dash;

        // Bottleneck alert
        _bottleneckAlert = CreateCard();
        _bottleneckAlert.Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x1a, 0x1a));
        _bottleneckAlert.Visibility = Visibility.Collapsed;
        var bp = new StackPanel();
        bp.Children.Add(new TextBlock { Text = "BOTTLENECK DETECTED", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Danger });
        _bottleneckText = new TextBlock { FontSize = 14, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        bp.Children.Add(_bottleneckText);
        _bottleneckAlert.Child = bp;
        dash.Children.Add(_bottleneckAlert);

        // Live monitor
        dash.Children.Add(new TextBlock { Text = "LIVE USAGE", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TextSecondary, Margin = new Thickness(8, 8, 8, 0) });
        dash.Children.Add(BuildLiveStrip());

        // Components
        dash.Children.Add(new TextBlock { Text = "COMPONENT MAP", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TextSecondary, Margin = new Thickness(8, 16, 8, 0) });

        var componentGrid = new Grid();
        componentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        componentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        componentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        componentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        (_cpuCard, _cpuName, _cpuScore, _cpuDetails, _cpuBottleneckBadge) = CreateComponentCard("CPU", CpuColor);
        Grid.SetColumn(_cpuCard, 0); Grid.SetRow(_cpuCard, 0);
        componentGrid.Children.Add(_cpuCard);

        (_gpuCard, _gpuName, _gpuScore, _gpuDetails, _gpuBottleneckBadge) = CreateComponentCard("GPU", GpuColor);
        Grid.SetColumn(_gpuCard, 1); Grid.SetRow(_gpuCard, 0);
        componentGrid.Children.Add(_gpuCard);

        (_ramCard, _ramName, _ramScore, _ramDetails, _ramBottleneckBadge) = CreateComponentCard("RAM", RamColor);
        Grid.SetColumn(_ramCard, 0); Grid.SetRow(_ramCard, 1);
        componentGrid.Children.Add(_ramCard);

        (_storageCard, _storageName, _storageScore, _storageDetails, _storageBottleneckBadge) = CreateComponentCard("STORAGE", StorageColor);
        Grid.SetColumn(_storageCard, 1); Grid.SetRow(_storageCard, 1);
        componentGrid.Children.Add(_storageCard);

        dash.Children.Add(componentGrid);

        // Upgrade recommendation
        _upgradeCard = CreateCard();
        _upgradeCard.Background = new SolidColorBrush(Color.FromRgb(0x12, 0x2a, 0x3f));
        _upgradeCard.Visibility = Visibility.Collapsed;
        var up = new StackPanel();
        up.Children.Add(new TextBlock { Text = "UPGRADE RECOMMENDATION", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = CpuColor });
        _upgradeText = new TextBlock { FontSize = 14, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        up.Children.Add(_upgradeText);
        _upgradeCard.Child = up;
        dash.Children.Add(_upgradeCard);

        // Optimize + restore
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 8) };
        _btnOptimize = CreateButton("Optimize for Normal Use", Success, BtnOptimize_Click);
        _btnOptimize.Padding = new Thickness(20, 14, 20, 14);
        _btnOptimize.FontSize = 16;
        _btnOptimize.FontWeight = FontWeights.Bold;
        _btnOptimize.Foreground = Brushes.White;
        _btnOptimize.Visibility = Visibility.Collapsed;

        _btnRestore = CreateButton("Restore Startup Apps", Accent, BtnRestore_Click);
        _btnRestore.Padding = new Thickness(16, 14, 16, 14);
        _btnRestore.FontSize = 14;
        _btnRestore.Margin = new Thickness(12, 4, 4, 4);
        _btnRestore.Visibility = Visibility.Collapsed;

        buttonRow.Children.Add(_btnOptimize);
        buttonRow.Children.Add(_btnRestore);
        dash.Children.Add(buttonRow);

        _optimizeResult = CreateCard();
        _optimizeResult.Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x3a, 0x1a));
        _optimizeResult.Visibility = Visibility.Collapsed;
        var op = new StackPanel();
        op.Children.Add(new TextBlock { Text = "OPTIMIZATION COMPLETE", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Success });
        _optimizeResultText = new TextBlock { FontSize = 14, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        op.Children.Add(_optimizeResultText);
        _optimizeResult.Child = op;
        dash.Children.Add(_optimizeResult);
    }

    private void BuildSearchArea(Grid mainGrid)
    {
        _searchSection = CreateCard(margin: new Thickness(16, 16, 16, 8));
        _searchSection.Visibility = Visibility.Collapsed;
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var searchLabel = new TextBlock
        {
            Text = "Can I Run It?",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };
        Grid.SetColumn(searchLabel, 0);

        _searchTextBox = new TextBox
        {
            Background = BgCard,
            Foreground = TextPrimary,
            BorderBrush = Accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            FontSize = 14,
            CaretBrush = Brushes.White,
            ToolTip = "Type a game or app name, e.g. GTA 6 or Adobe Premiere Pro"
        };
        Grid.SetColumn(_searchTextBox, 1);

        _btnSearch = CreateButton("Search", Accent, BtnSearch_Click);
        _btnSearch.Margin = new Thickness(8, 0, 0, 0);
        _btnSearch.Padding = new Thickness(16, 10, 16, 10);
        Grid.SetColumn(_btnSearch, 2);

        searchGrid.Children.Add(searchLabel);
        searchGrid.Children.Add(_searchTextBox);
        searchGrid.Children.Add(_btnSearch);
        _searchSection.Child = searchGrid;
        Grid.SetRow(_searchSection, 0);
        mainGrid.Children.Add(_searchSection);

        _searchResultCard = CreateCard(margin: new Thickness(16, 8, 16, 8));
        _searchResultCard.Visibility = Visibility.Collapsed;
        var panel = new StackPanel();
        _searchResultTitle = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = TextPrimary };
        _searchResultDetails = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var close = CreateButton("Close", Accent, (_, _) => _searchResultCard.Visibility = Visibility.Collapsed);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Padding = new Thickness(12, 6, 12, 6);
        close.FontSize = 12;
        close.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(_searchResultTitle);
        panel.Children.Add(_searchResultDetails);
        panel.Children.Add(close);
        _searchResultCard.Child = panel;
        Grid.SetRow(_searchResultCard, 0);
        mainGrid.Children.Add(_searchResultCard);
    }

    private Border BuildLiveStrip()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border Make(string label, Brush color, out ProgressBar bar, out TextBlock text)
        {
            var card = CreateCard(padding: 14);
            var sp = new StackPanel();
            var head = new Grid();
            head.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = TextSecondary });
            text = new TextBlock { Text = "--", FontSize = 11, Foreground = color, HorizontalAlignment = HorizontalAlignment.Right };
            head.Children.Add(text);
            bar = new ProgressBar { Height = 6, Margin = new Thickness(0, 8, 0, 0), Maximum = 100, Foreground = color, Background = new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x4a)), BorderThickness = new Thickness(0) };
            sp.Children.Add(head);
            sp.Children.Add(bar);
            card.Child = sp;
            return card;
        }

        var cpu = Make("CPU", CpuColor, out _liveCpuBar, out _liveCpuText);
        var ram = Make("RAM", RamColor, out _liveRamBar, out _liveRamText);
        var disk = Make("DISK", StorageColor, out _liveDiskBar, out _liveDiskText);
        Grid.SetColumn(cpu, 0);
        Grid.SetColumn(ram, 1);
        Grid.SetColumn(disk, 2);

        grid.Children.Add(cpu);
        grid.Children.Add(ram);
        grid.Children.Add(disk);

        var wrap = CreateCard(padding: 0);
        wrap.Padding = new Thickness(0);
        wrap.Background = Brushes.Transparent;
        wrap.Child = grid;
        return wrap;
    }

    private Border CreateCard(Thickness? margin = null, double padding = 20)
    {
        return new Border
        {
            Background = BgCard,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(padding),
            Margin = margin ?? new Thickness(8)
        };
    }

    private Button CreateButton(string content, Brush background, RoutedEventHandler handler)
    {
        var btn = new Button
        {
            Content = content,
            Background = background,
            Foreground = Brushes.White,
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(4),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        btn.Click += handler;
        return btn;
    }

    private (Border card, TextBlock name, ProgressBar score, StackPanel details, TextBlock bottleneckBadge)
        CreateComponentCard(string label, Brush accent)
    {
        var card = CreateCard();
        var panel = new StackPanel();

        var headerPanel = new Grid();
        headerPanel.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = accent });
        var bottleneckBadge = new TextBlock
        {
            Text = "BOTTLENECK",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Danger,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed
        };
        headerPanel.Children.Add(bottleneckBadge);

        var name = new TextBlock { Text = "--", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis };
        var score = new ProgressBar
        {
            Height = 8,
            Background = new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x4a)),
            Foreground = Success,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 12, 0, 0)
        };
        var details = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        panel.Children.Add(headerPanel);
        panel.Children.Add(name);
        panel.Children.Add(score);
        panel.Children.Add(details);
        card.Child = panel;

        return (card, name, score, details, bottleneckBadge);
    }

    // ==================== SCANNING ====================

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) => await RunScan();

    private async Task RunScan()
    {
        if (_scanning) return;
        _scanning = true;
        _btnRefresh.IsEnabled = false;
        _scanStatusText.Text = "Scanning hardware...";
        _scanProgress.Visibility = Visibility.Visible;

        try
        {
            var profile = await Task.Run(() => _scanner.Scan());
            _profile = profile;

            UpdateDashboard();
            _scanStatusText.Text = $"Last scan: {profile.ScannedAt:HH:mm:ss}";
            _adminBadge.Text = profile.IsAdmin ? "Administrator Mode" : "Standard Mode (limited)";
            _adminBadge.Foreground = profile.IsAdmin ? Success : Warning;
            _powerNote.Text = BuildPowerNote(profile);
            _powerNote.Foreground = profile.OnAcPower && profile.PowerPlan != "Power saver" ? TextSecondary : Warning;

            _liveTimer.Start();
            _ = UpdateLiveAsync();
        }
        catch (Exception ex)
        {
            _scanStatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _scanProgress.Visibility = Visibility.Collapsed;
            _btnRefresh.IsEnabled = true;
            _scanning = false;
        }
    }

    private static string BuildPowerNote(SystemProfile p)
    {
        if (p.PowerPlan == "Unknown") return "";
        var ac = p.OnAcPower ? "plugged in" : "on battery";
        return $"Laptop tip: {p.PowerPlan} power plan, {ac}.";
    }

    private void UpdateDashboard()
    {
        if (_profile == null) return;

        UpdateCard(_cpuName, _cpuScore, _cpuDetails, _cpuBottleneckBadge, _cpuCard, _profile.Cpu);
        UpdateCard(_gpuName, _gpuScore, _gpuDetails, _gpuBottleneckBadge, _gpuCard, _profile.Gpu);
        UpdateCard(_ramName, _ramScore, _ramDetails, _ramBottleneckBadge, _ramCard, _profile.Ram);
        UpdateCard(_storageName, _storageScore, _storageDetails, _storageBottleneckBadge, _storageCard, _profile.Storage);

        // Show the integrated GPU on the primary GPU card, if present.
        if (_profile.IntegratedGpu != null)
        {
            _gpuDetails.Children.Add(new TextBlock
            {
                Text = $"Also present: {_profile.IntegratedGpu.Name} (integrated)",
                FontSize = 12,
                Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        ProfileEvaluator.Evaluate(_profile, _currentProfile);

        _cpuBottleneckBadge.Visibility = _profile.Cpu.IsBottleneck ? Visibility.Visible : Visibility.Collapsed;
        _gpuBottleneckBadge.Visibility = _profile.Gpu.IsBottleneck ? Visibility.Visible : Visibility.Collapsed;
        _ramBottleneckBadge.Visibility = _profile.Ram.IsBottleneck ? Visibility.Visible : Visibility.Collapsed;
        _storageBottleneckBadge.Visibility = _profile.Storage.IsBottleneck ? Visibility.Visible : Visibility.Collapsed;

        var bottleneckMsg = ProfileEvaluator.GetBottleneckMessage(_profile, _currentProfile);
        if (bottleneckMsg.StartsWith("This part"))
        {
            _bottleneckText.Text = bottleneckMsg;
            _bottleneckAlert.Visibility = Visibility.Visible;
        }
        else
        {
            _bottleneckAlert.Visibility = Visibility.Collapsed;
        }

        // Upgrade recommendation
        _upgradeText.Text = UpgradeAdvisor.Build(_profile, _currentProfile, _tiers);
        _upgradeCard.Visibility = Visibility.Visible;

        var isNormalAdmin = _currentProfile == UsageProfile.Normal && _profile.IsAdmin;
        _btnOptimize.Visibility = isNormalAdmin ? Visibility.Visible : Visibility.Collapsed;
        _btnRestore.Visibility = isNormalAdmin ? Visibility.Visible : Visibility.Collapsed;
        _searchSection.Visibility = _currentProfile is UsageProfile.Gaming or UsageProfile.Graphic
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCard(TextBlock nameBlock, ProgressBar scoreBar, StackPanel detailsPanel,
        TextBlock bottleneckBadge, Border card, ComponentInfo info)
    {
        nameBlock.Text = info.Name;
        scoreBar.Value = info.Score;

        if (info.Score >= 70) scoreBar.Foreground = Success;
        else if (info.Score >= 40) scoreBar.Foreground = Warning;
        else scoreBar.Foreground = Danger;

        card.BorderBrush = info.IsBottleneck ? Danger : Brushes.Transparent;
        card.BorderThickness = info.IsBottleneck ? new Thickness(3) : new Thickness(0);
        card.ToolTip = $"{info.Type} score: {info.Score:F0}/100\n{info.Tooltip}";

        detailsPanel.Children.Clear();
        foreach (var kvp in info.Details)
        {
            detailsPanel.Children.Add(new TextBlock
            {
                Text = $"{kvp.Key}: {kvp.Value}",
                FontSize = 12,
                Foreground = TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
    }

    // ==================== LIVE MONITOR ====================

    private async void LiveTick(object? sender, EventArgs e) => await UpdateLiveAsync();

    private async Task UpdateLiveAsync()
    {
        if (_profile == null) return;
        try
        {
            var stats = await Task.Run(() => _liveMonitor.Sample());

            _liveCpuBar.Value = Math.Clamp(stats.CpuUsagePercent, 0, 100);
            _liveCpuText.Text = $"{stats.CpuUsagePercent:F0}%";

            _liveRamBar.Value = Math.Clamp(stats.RamUsagePercent, 0, 100);
            _liveRamText.Text = $"{stats.RamUsedGb:F1} / {stats.RamTotalGb:F0} GB";

            _liveDiskBar.Value = Math.Clamp(stats.DiskActivityPercent, 0, 100);
            _liveDiskText.Text = $"{stats.DiskActivityPercent:F0}%";
        }
        catch { }
    }

    // ==================== PROFILE SWITCHING ====================

    private void BtnGaming_Click(object sender, RoutedEventArgs e) => SwitchProfile(UsageProfile.Gaming);
    private void BtnGraphic_Click(object sender, RoutedEventArgs e) => SwitchProfile(UsageProfile.Graphic);
    private void BtnNormal_Click(object sender, RoutedEventArgs e) => SwitchProfile(UsageProfile.Normal);
    private void SwitchProfile(UsageProfile profile) { _currentProfile = profile; UpdateDashboard(); }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RunScan();

    // ==================== OPTIMIZER ====================

    private void BtnOptimize_Click(object sender, RoutedEventArgs e)
    {
        var preview = _optimizer.BuildPreview();

        if (!preview.HasAnythingToDo)
        {
            _optimizeResultText.Text = "Nothing to optimize — your system is already clean.";
            _optimizeResult.Visibility = Visibility.Visible;
            return;
        }

        var msg = "This will make the following changes:\n\n";
        if (preview.ProcessesToClose.Count > 0)
            msg += $"• Close {preview.ProcessesToClose.Count} background app(s):\n   {string.Join(", ", preview.ProcessesToClose.Take(10))}" +
                   (preview.ProcessesToClose.Count > 10 ? " ..." : "") + "\n\n";
        if (preview.StartupAppsToDisable.Count > 0)
            msg += $"• Disable {preview.StartupAppsToDisable.Count} startup program(s):\n   {string.Join(", ", preview.StartupAppsToDisable.Take(10))}" +
                   (preview.StartupAppsToDisable.Count > 10 ? " ..." : "") + "\n\n";
        if (preview.TempFilesFound > 0)
            msg += $"• Delete about {preview.TempFilesFound} temporary file(s)\n\n";

        msg += "These actions are safe and can be undone (startup apps can be restored).\n\nAre you sure you want to continue?";

        var choice = MessageBox.Show(msg, "Confirm Optimization", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes) return;

        var result = _optimizer.OptimizeForNormalUse();

        _optimizeResultText.Text =
            $"Cleared {result.TempFilesCleared} temp files, " +
            $"disabled {result.StartupAppsDisabled} startup apps, " +
            $"closed {result.ProcessesKilled} background processes.";
        _optimizeResult.Visibility = Visibility.Visible;
    }

    private void BtnRestore_Click(object sender, RoutedEventArgs e)
    {
        var restored = _optimizer.RestoreStartupApps();
        _optimizeResultText.Text = restored > 0
            ? $"Restored {restored} startup app(s). They will run again next time you sign in."
            : "No disabled startup apps found to restore.";
        _optimizeResult.Visibility = Visibility.Visible;
    }

    // ==================== SEARCH ====================

    private async void BtnSearch_Click(object sender, RoutedEventArgs e) => await RunSearch();

    private async Task RunSearch()
    {
        var query = _searchTextBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        var req = await _db.FindAsync(query);
        if (req == null)
        {
            _searchResultTitle.Text = $"No results for '{query}'";
            _searchResultTitle.Foreground = TextSecondary;
            _searchResultDetails.Children.Clear();
            _searchResultCard.Visibility = Visibility.Visible;
            return;
        }

        var runResult = _db.CanRun(_profile!, req);

        _searchResultTitle.Text = req.Name;
        _searchResultTitle.Foreground = runResult switch
        {
            RunResult.Ready => Success,
            RunResult.LowSettings => Warning,
            _ => Danger
        };

        _searchResultDetails.Children.Clear();
        var icon = runResult switch
        {
            RunResult.Ready => "READY",
            RunResult.LowSettings => "LOW SETTINGS",
            _ => "UPGRADE REQUIRED"
        };

        _searchResultDetails.Children.Add(new TextBlock
        {
            Text = $"[{icon}] Your system {(runResult == RunResult.Ready ? "meets" : runResult == RunResult.LowSettings ? "partially meets" : "does not meet")} the requirements for {req.Name}.",
            FontSize = 13,
            Foreground = TextPrimary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        AddRequirementRow("Minimum", req.Min);
        AddRequirementRow("Recommended", req.Rec);

        _searchResultCard.Visibility = Visibility.Visible;
    }

    private void AddRequirementRow(string label, Dictionary<string, string> specs)
    {
        var line = string.Join("  |  ", specs.Select(kv => $"{kv.Key}: {kv.Value}"));
        _searchResultDetails.Children.Add(new TextBlock
        {
            Text = $"{label}: {line}",
            FontSize = 12,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
    }
}
