using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GhubLite.App.ViewModels;
using Forms = System.Windows.Forms;

namespace GhubLite.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem[] _profileItems = new Forms.ToolStripMenuItem[5];
    private Forms.ToolStripMenuItem? _autoSwitchItem;
    private bool _exiting;
    private bool _hiddenBalloonShown;
    private bool _lowBatteryNotified;

    public MainWindow()
    {
        InitializeComponent();
        var work = SystemParameters.WorkArea;
        Width = Math.Min(520, Math.Max(440, work.Width - 40));
        Height = Math.Min(820, Math.Max(560, work.Height - 40));
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _trayIcon = CreateTrayIcon();
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        Application.Current.SessionEnding += (_, _) => _exiting = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Some launchers (including hidden terminals) pass SW_SHOWMINIMIZED in STARTUPINFO.
        // A tray utility should still open visibly on first launch.
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });

        await _viewModel.InitializeAsync();
        MouseScroll.ScrollToTop();
        _ = Dispatcher.BeginInvoke(new Action(() => MouseScroll.ScrollToTop()), DispatcherPriority.ContextIdle);
        UpdateTrayMenu();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var icon = new Forms.NotifyIcon
        {
            Text = "GhubLite — PRO X SUPERLIGHT",
            Visible = true,
        };

        try
        {
            icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        }
        catch
        {
            icon.Icon = SystemIcons.Application;
        }

        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("打开 GhubLite", null, (_, _) => ShowWindow())
        {
            Font = new Font(menu.Font, System.Drawing.FontStyle.Bold),
        };
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());

        for (int i = 1; i <= 5; i++)
        {
            int index = i;
            var item = new Forms.ToolStripMenuItem($"P{i} 配置", null, (_, _) => _ = _viewModel.SwitchProfileAsync(index));
            _profileItems[i - 1] = item;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        _autoSwitchItem = new Forms.ToolStripMenuItem("按应用自动切换", null, (_, _) =>
        {
            _viewModel.AutoSwitch = !_viewModel.AutoSwitch;
            UpdateTrayMenu();
        })
        {
            CheckOnClick = false,
        };
        menu.Items.Add(_autoSwitchItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("退出", null, (_, _) => ExitApp()));

        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => ShowWindow();
        return icon;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.BatteryPercent))
        {
            if (_viewModel.BatteryPercent is > 0 and <= 20 && !_lowBatteryNotified && !_viewModel.BatteryStatusText.Contains("充电"))
            {
                _lowBatteryNotified = true;
                _trayIcon.ShowBalloonTip(3500, "GhubLite", $"鼠标电量 {_viewModel.BatteryPercent}%，建议充电。", Forms.ToolTipIcon.Warning);
            }
        }

        if (e.PropertyName is nameof(MainViewModel.ActiveProfileLabel) or nameof(MainViewModel.AutoSwitch))
        {
            UpdateTrayMenu();
        }
    }

    private void UpdateTrayMenu()
    {
        for (int i = 1; i <= 5; i++)
        {
            _profileItems[i - 1].Checked = _viewModel.ActiveProfileLabel == $"P{i} 生效";
        }

        if (_autoSwitchItem is not null)
        {
            _autoSwitchItem.Checked = _viewModel.AutoSwitch;
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MainTabs is null)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            Keyboard.ClearFocus();
            GetSelectedScroll()?.ScrollToTop();
        }), DispatcherPriority.ContextIdle);
    }

    private ScrollViewer? GetSelectedScroll() => MainTabs.SelectedIndex switch
    {
        0 => MouseScroll,
        1 => AppsScroll,
        2 => SettingsScroll,
        _ => null,
    };

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void HideToTray()
    {
        if (!_viewModel.MinimizeToTray)
        {
            ExitApp();
            return;
        }

        Hide();
        if (!_hiddenBalloonShown)
        {
            _hiddenBalloonShown = true;
            _trayIcon.ShowBalloonTip(2500, "GhubLite 还在运行", "双击托盘图标可以重新打开。", Forms.ToolTipIcon.Info);
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private async void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _trayIcon.Visible = false;
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        try
        {
            await _viewModel.ShutdownAsync();
        }
        finally
        {
            _trayIcon.Dispose();
            Application.Current.Shutdown();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }
}
