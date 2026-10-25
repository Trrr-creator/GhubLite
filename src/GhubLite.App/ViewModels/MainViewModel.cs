using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GhubLite.Core.Hidpp;
using GhubLite.Core.Models;
using GhubLite.Core.Services;
using Microsoft.Win32;

namespace GhubLite.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
	private sealed record EditSnapshot(int Index, bool Enabled, int Dpi, int RateMs, string Name, int[] Slots, int DefaultIndex);

	private sealed record HardwareSyncResult(bool Success, int ActualDpi, int ActualRateMs, bool ProfileOk);

	private sealed record DeviceSnapshot(DeviceInfo Info, byte DeviceIndex, BatteryInfo Battery, ushort Dpi, int RateMs, IReadOnlyList<int> RateOptions, ProfileDescriptor Descriptor, IReadOnlyList<ControlHeader> Headers, Dictionary<int, OnboardProfile> Profiles, int CurrentProfile, int OnboardMode);

	private static readonly SolidColorBrush SuccessBrush = Frozen("#48E0A4");

	private static readonly SolidColorBrush WarningBrush = Frozen("#FFC857");

	private static readonly SolidColorBrush DangerBrush = Frozen("#FF6B7A");

	private static readonly SolidColorBrush AccentBrush = Frozen("#39D0FF");

	private readonly ConfigStore _store;

	private readonly BackupService _backups;

	private readonly ForegroundWatcher _watcher;

	private readonly Dispatcher _dispatcher;

	private readonly SemaphoreSlim _deviceGate = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _switchGate = new SemaphoreSlim(1, 1);

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private HidppDevice? _device;

	private AppConfig _config = new AppConfig();

	private bool _initialized;

	private bool _suppress;

	private bool _connected;

	private bool _busy;

	private bool _dirty;

	private bool _autoSwitch;

	private bool _startWithWindows;

	private bool _minimizeToTray = true;

	private bool _sessionBackupDone;

	private bool _disposed;

	private string? _sessionBackupPath;

	private int _activeProfile = 1;

	private string _deviceName = "PRO X SUPERLIGHT";

	private string _connectionText = "正在连接…";

	private string _protocolText = "HID++ 2.0";

	private string _statusText = "正在连接鼠标…";

	private string _deviceDetails = "";

	private string _backupStatus = "";

	private string _batteryText = "--";

	private string _batteryStatusText = "读取中";

	private string _batteryLevelText = "";

	private string _batteryUpdatedText = "";

	private int _batteryPercent;

	private Brush _batteryBrush = AccentBrush;

	private Brush _connectionBrush = DangerBrush;

	private string _toastText = "";

	private bool _toastVisible;

	private Brush _toastBrush = SuccessBrush;

	private CancellationTokenSource? _toastCts;

	private string _profileName = "";

	private int _dpi = 800;

	private int _selectedDpiSlotIndex = 1;

	private ReportRateVm? _selectedReportRate;

	private ProfileVm? _selectedProfile;

	private bool _autoApplyRunning;

	private bool _autoApplyPending;

	private bool _autoApplyImmediate;

	private bool _autoSaveRunning;

	private bool _autoSavePending;

	public ObservableCollection<ProfileVm> Profiles { get; } = new ObservableCollection<ProfileVm>();

	public ObservableCollection<RuleVm> Rules { get; } = new ObservableCollection<RuleVm>();

	public ObservableCollection<ReportRateVm> ReportRateOptions { get; } = new ObservableCollection<ReportRateVm>();

	public ObservableCollection<DpiSlotVm> DpiSlots { get; } = new ObservableCollection<DpiSlotVm>();

	public RelayCommand SaveProfileCommand { get; }

	public RelayCommand SelectDpiSlotCommand { get; }

	public RelayCommand SelectReportRateCommand { get; }

	public AsyncRelayCommand EnableProfilesCommand { get; }

	public AsyncRelayCommand AddCurrentAppCommand { get; }

	public AsyncRelayCommand<ProfileVm> AssignCurrentAppCommand { get; }

	public RelayCommand RemoveRuleCommand { get; }

	public AsyncRelayCommand BackupCommand { get; }

	public AsyncRelayCommand RestoreCommand { get; }

	public AsyncRelayCommand ReconnectCommand { get; }

	public AsyncRelayCommand RefreshCommand { get; }

	public string DeviceName
	{
		get
		{
			return _deviceName;
		}
		private set
		{
			Set(ref _deviceName, value, "DeviceName");
		}
	}

	public string ConnectionText
	{
		get
		{
			return _connectionText;
		}
		private set
		{
			Set(ref _connectionText, value, "ConnectionText");
		}
	}

	public string ProtocolText
	{
		get
		{
			return _protocolText;
		}
		private set
		{
			Set(ref _protocolText, value, "ProtocolText");
		}
	}

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		private set
		{
			Set(ref _statusText, value, "StatusText");
		}
	}

	public string ToastText
	{
		get
		{
			return _toastText;
		}
		private set
		{
			Set(ref _toastText, value, "ToastText");
		}
	}

	public bool ToastVisible
	{
		get
		{
			return _toastVisible;
		}
		private set
		{
			Set(ref _toastVisible, value, "ToastVisible");
		}
	}

	public Brush ToastBrush
	{
		get
		{
			return _toastBrush;
		}
		private set
		{
			Set(ref _toastBrush, value, "ToastBrush");
		}
	}

	public string DeviceDetails
	{
		get
		{
			return _deviceDetails;
		}
		private set
		{
			Set(ref _deviceDetails, value, "DeviceDetails");
		}
	}

	public string BackupStatus
	{
		get
		{
			return _backupStatus;
		}
		private set
		{
			Set(ref _backupStatus, value, "BackupStatus");
		}
	}

	public string BatteryText
	{
		get
		{
			return _batteryText;
		}
		private set
		{
			Set(ref _batteryText, value, "BatteryText");
		}
	}

	public string BatteryStatusText
	{
		get
		{
			return _batteryStatusText;
		}
		private set
		{
			Set(ref _batteryStatusText, value, "BatteryStatusText");
		}
	}

	public string BatteryLevelText
	{
		get
		{
			return _batteryLevelText;
		}
		private set
		{
			Set(ref _batteryLevelText, value, "BatteryLevelText");
		}
	}

	public string BatteryUpdatedText
	{
		get
		{
			return _batteryUpdatedText;
		}
		private set
		{
			Set(ref _batteryUpdatedText, value, "BatteryUpdatedText");
		}
	}

	public int BatteryPercent
	{
		get
		{
			return _batteryPercent;
		}
		private set
		{
			if (Set(ref _batteryPercent, value, "BatteryPercent"))
			{
				OnPropertyChanged("BatteryProgress");
			}
		}
	}

	public double BatteryProgress => BatteryPercent;

	public Brush BatteryBrush
	{
		get
		{
			return _batteryBrush;
		}
		private set
		{
			Set(ref _batteryBrush, value, "BatteryBrush");
		}
	}

	public Brush ConnectionBrush
	{
		get
		{
			return _connectionBrush;
		}
		private set
		{
			Set(ref _connectionBrush, value, "ConnectionBrush");
		}
	}

	public bool IsConnected
	{
		get
		{
			return _connected;
		}
		private set
		{
			if (Set(ref _connected, value, "IsConnected"))
			{
				ConnectionBrush = (value ? SuccessBrush : DangerBrush);
				RaiseCommands();
			}
		}
	}

	public bool IsBusy
	{
		get
		{
			return _busy;
		}
		private set
		{
			if (Set(ref _busy, value, "IsBusy"))
			{
				RaiseCommands();
			}
		}
	}

	public bool IsDirty
	{
		get
		{
			return _dirty;
		}
		private set
		{
			if (Set(ref _dirty, value, "IsDirty"))
			{
				OnPropertyChanged("EditStateText");
				SaveProfileCommand.RaiseCanExecuteChanged();
			}
		}
	}

	public bool AutoSwitch
	{
		get
		{
			return _autoSwitch;
		}
		set
		{
			if (Set(ref _autoSwitch, value, "AutoSwitch") && !_suppress)
			{
				_config.AutoSwitch = value;
				_store.Save(_config);
				_watcher.Enabled = value;
				_watcher.Reset();
				StatusText = (value ? "按应用自动切换已开启" : "按应用自动切换已关闭");
				if (value)
				{
					ApplyForProcessAsync(ForegroundWatcher.GetForegroundProcessName(), force: true);
				}
			}
		}
	}

	public bool StartWithWindows
	{
		get
		{
			return _startWithWindows;
		}
		set
		{
			if (!Set(ref _startWithWindows, value, "StartWithWindows") || _suppress)
			{
				return;
			}
			try
			{
				StartupManager.Set(value);
				_config.StartWithWindows = value;
				_store.Save(_config);
				StatusText = (value ? "已加入开机启动" : "已取消开机启动");
			}
			catch (Exception ex)
			{
				_suppress = true;
				Set(ref _startWithWindows, !value, "StartWithWindows");
				_suppress = false;
				StatusText = "开机启动设置失败: " + ex.Message;
			}
		}
	}

	public bool MinimizeToTray
	{
		get
		{
			return _minimizeToTray;
		}
		set
		{
			if (Set(ref _minimizeToTray, value, "MinimizeToTray") && !_suppress)
			{
				_config.MinimizeToTray = value;
				_store.Save(_config);
			}
		}
	}

	public ProfileVm? SelectedProfile
	{
		get
		{
			return _selectedProfile;
		}
		set
		{
			if (value == null || _selectedProfile == value)
			{
				return;
			}
			if (IsDirty && !_suppress && _device != null && _selectedProfile != null)
			{
				EditSnapshot editSnapshot = CaptureEditSnapshot(_selectedProfile);
				_autoSavePending = false;
				if ((object)editSnapshot != null)
				{
					SaveSnapshotAsync(editSnapshot, auto: true);
				}
			}
			Set(ref _selectedProfile, value, "SelectedProfile");
			LoadSelectedProfile();
			StatusText = $"正在编辑 P{value.Index}";
		}
	}

	public string ProfileName
	{
		get
		{
			return _profileName;
		}
		set
		{
			if (Set(ref _profileName, value, "ProfileName") && !_suppress)
			{
				OnEditableChanged(immediate: false);
			}
		}
	}

	public int Dpi
	{
		get
		{
			return _dpi;
		}
		set
		{
			int num = Math.Clamp(value, 100, 25600);
			num = (int)(Math.Round((double)num / 50.0) * 50.0);
			if (Set(ref _dpi, num, "Dpi") && !_suppress && _selectedDpiSlotIndex >= 0 && _selectedDpiSlotIndex < DpiSlots.Count)
			{
				DpiSlots[_selectedDpiSlotIndex].Value = num;
				OnEditableChanged(immediate: false);
			}
		}
	}

	public int SelectedDpiSlotIndex
	{
		get
		{
			return _selectedDpiSlotIndex;
		}
		set
		{
			int num = Math.Clamp(value, 0, Math.Max(0, DpiSlots.Count - 1));
			if (Set(ref _selectedDpiSlotIndex, num, "SelectedDpiSlotIndex"))
			{
				OnPropertyChanged("SelectedDpiSlotNumber");
				if (!_suppress && num < DpiSlots.Count)
				{
					Dpi = DpiSlots[num].Value;
					OnEditableChanged(immediate: true);
				}

				UpdateDpiSelection();
			}
		}
	}

	public ReportRateVm? SelectedReportRate
	{
		get
		{
			return _selectedReportRate;
		}
		set
		{
			if (Set(ref _selectedReportRate, value, "SelectedReportRate") && !_suppress)
			{
				OnEditableChanged(immediate: true);
			}

			UpdateRateSelection();
		}
	}

	public string EditStateText
	{
		get
		{
			if (!IsDirty)
			{
				return "已同步";
			}
			return "自动保存中…";
		}
	}

	public int SelectedDpiSlotNumber => SelectedDpiSlotIndex + 1;

	public string ActiveProfileLabel => $"P{_activeProfile} 生效";

	public string ActiveProfileName => Profiles.FirstOrDefault((ProfileVm p) => p.Index == _activeProfile)?.NameDisplay ?? "";

	public bool HasNoRules => Rules.Count == 0;

	public bool HasRules => Rules.Count > 0;

	public string RulesCountText => $"{Rules.Count} 条规则";

	public bool ShowEnableProfiles => Profiles.Any((ProfileVm p) => !p.IsEnabled);

	public string EnableProfilesHint
	{
		get
		{
			if (!ShowEnableProfiles)
			{
				return "5 个板载配置已启用";
			}
			return "P2–P5 尚未启用，启用后才能按应用切换";
		}
	}

	public MainViewModel()
	{
		_store = new ConfigStore();
		_backups = new BackupService(_store);
		Application current = Application.Current;
		_dispatcher = ((current != null) ? ((DispatcherObject)current).Dispatcher : null) ?? Dispatcher.CurrentDispatcher;
		_watcher = new ForegroundWatcher();
		_watcher.ForegroundProcessChanged += OnForegroundProcessChanged;
		for (int i = 1; i <= 5; i++)
		{
			Profiles.Add(new ProfileVm(i));
		}
		int[] array = new int[4] { 1, 2, 4, 8 };
		foreach (int ms in array)
		{
			ReportRateOptions.Add(new ReportRateVm(ms));
		}
		array = new int[5] { 400, 800, 1600, 3200, 6400 };
		foreach (int value in array)
		{
			DpiSlots.Add(new DpiSlotVm(value));
		}
		SaveProfileCommand = new RelayCommand(() =>
		{
			SaveSelectedProfileAsync();
		}, () => IsConnected && !IsBusy && IsDirty);
		SelectDpiSlotCommand = new RelayCommand((object? parameter) =>
		{
			if (parameter is DpiSlotVm slot)
			{
				SelectDpiSlot(slot);
			}
		});
		SelectReportRateCommand = new RelayCommand((object? parameter) =>
		{
			if (parameter is ReportRateVm rate)
			{
				SelectReportRate(rate);
			}
		});
		EnableProfilesCommand = new AsyncRelayCommand(EnableProfilesAsync, () => IsConnected && !IsBusy);
		AddCurrentAppCommand = new AsyncRelayCommand(() => AddCurrentAppAsync(null), () => IsConnected && !IsBusy);
		AssignCurrentAppCommand = new AsyncRelayCommand<ProfileVm>((ProfileVm? profile) => AddCurrentAppAsync(profile), (ProfileVm? _) => IsConnected && !IsBusy);
		RemoveRuleCommand = new RelayCommand((object? parameter) =>
		{
			if (parameter is RuleVm ruleVm)
			{
				Rules.Remove(ruleVm);
				PersistRules();
				StatusText = "已移除规则 " + ruleVm.ProcessName;
			}
		});
		BackupCommand = new AsyncRelayCommand(BackupToFileAsync, () => IsConnected && !IsBusy);
		RestoreCommand = new AsyncRelayCommand(RestoreFromFileAsync, () => IsConnected && !IsBusy);
		ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, () => !IsBusy);
		RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsConnected && !IsBusy);
		SelectedProfile = Profiles[0];
		SelectedReportRate = ReportRateOptions[0];
	}

	public async Task InitializeAsync()
	{
		if (!_initialized)
		{
			_initialized = true;
			_config = _store.Load();
			_autoSwitch = _config.AutoSwitch;
			_startWithWindows = _config.StartWithWindows;
			_minimizeToTray = _config.MinimizeToTray;
			OnPropertyChanged("AutoSwitch");
			OnPropertyChanged("StartWithWindows");
			OnPropertyChanged("MinimizeToTray");
			_watcher.Enabled = _config.AutoSwitch;
			LoadRules();
			await ConnectAndRefreshAsync();
			BatteryLoopAsync();
			await ApplyForProcessAsync(ForegroundWatcher.GetForegroundProcessName(), force: true);
		}
	}

	private async Task ConnectAndRefreshAsync()
	{
		SetBusy(busy: true, "正在连接鼠标…");
		try
		{
			HidppDevice device = await Task.Run((Func<HidppDevice>)HidppDevice.Connect);
			if (device == null)
			{
				Disconnect("未找到鼠标，请检查接收器或 USB 线");
				return;
			}
			_device?.Dispose();
			_device = device;
			IsConnected = true;
			DeviceSnapshot snapshot;
			try
			{
				snapshot = await Task.Run(() => ReadSnapshot(device));
			}
			catch
			{
				snapshot = await Task.Run(() =>
				{
					device.ApplyProfile(device.GetCurrentProfile());
					return ReadSnapshot(device);
				});
			}
			ApplySnapshot(snapshot);
			StatusText = "已连接";
		}
		catch (Exception ex)
		{
			Disconnect("连接失败");
			StatusText = "连接失败: " + ex.Message;
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private static DeviceSnapshot ReadSnapshot(HidppDevice device)
	{
		int mode = 0;
		try
		{
			device.EnsureHostMode();
			mode = device.ReadOnboardMode();
		}
		catch
		{
			// The verification step will surface the failure if host mode cannot be entered.
		}

		int currentProfile;
		try
		{
			currentProfile = Math.Clamp(device.GetCurrentProfile(), 1, 5);
		}
		catch
		{
			currentProfile = 1;
		}
		try
		{
			device.ApplyProfile(currentProfile);
		}
		catch
		{
		}
		DeviceInfo info = device.Info;
		BatteryInfo battery = device.ReadBattery();
		int rateMs = device.ReadReportRateMs();
		IReadOnlyList<int> rateOptions = device.ReadReportRateOptions();
		ProfileDescriptor descriptor = device.Descriptor;
		IReadOnlyList<ControlHeader> headers = device.ReadControlHeaders();
		Dictionary<int, OnboardProfile> dictionary = new Dictionary<int, OnboardProfile>();
		for (int i = 1; i <= 5; i++)
		{
			dictionary[i] = device.ReadProfile(i);
		}
		return new DeviceSnapshot(info, device.DeviceIndex, battery, device.ReadDpi(), rateMs, rateOptions, descriptor, headers, dictionary, currentProfile, mode);
	}

	private void ApplySnapshot(DeviceSnapshot snapshot)
	{
		DeviceName = FormatDeviceName(snapshot.Info.Name);
		ConnectionText = snapshot.Info.ConnectionText;
		ProtocolText = snapshot.Info.ProtocolText;
		ReportRateOptions.Clear();
		foreach (int rateOption in snapshot.RateOptions)
		{
			ReportRateOptions.Add(new ReportRateVm(rateOption));
		}
		foreach (KeyValuePair<int, OnboardProfile> pair in snapshot.Profiles)
		{
			ProfileVm profileVm = Profiles.First((ProfileVm p) => p.Index == pair.Key);
			profileVm.Apply(enabled: snapshot.Headers.Any((ControlHeader h) => h.Sector == pair.Key && h.Enabled), slots: pair.Value.DpiSlots, defaultIndex: pair.Value.DefaultDpiIndex, reportRateMs: pair.Value.ReportRateMs, name: pair.Value.Name);
			profileVm.IsActive = pair.Key == snapshot.CurrentProfile;
		}
		_activeProfile = snapshot.CurrentProfile;
		OnPropertyChanged("ActiveProfileLabel");
		OnPropertyChanged("ActiveProfileName");
		ApplyBattery(snapshot.Battery);
		ProfileVm selectedProfile = Profiles.FirstOrDefault((ProfileVm p) => p.Index == snapshot.CurrentProfile) ?? Profiles[0];
		_suppress = true;
		SelectedProfile = selectedProfile;
		_suppress = false;
		LoadSelectedProfile();
		_suppress = true;
		Dpi = snapshot.Dpi;
		SelectedReportRate = ReportRateOptions.FirstOrDefault((ReportRateVm r) => r.Ms == snapshot.RateMs) ?? ReportRateOptions[0];
		_suppress = false;
		DeviceDetails = BuildDetails(snapshot);
		OnPropertyChanged("ShowEnableProfiles");
		OnPropertyChanged("EnableProfilesHint");
		RaiseCommands();
	}

	private void ApplyBattery(BatteryInfo battery)
	{
		BatteryPercent = battery.Percentage;
		BatteryText = ((battery.Percentage > 0) ? $"{battery.Percentage}%" : "--");
		BatteryStatusText = battery.StatusText;
		BatteryLevelText = battery.LevelText;
		BatteryUpdatedText = $"更新于 {DateTime.Now:HH:mm}";
		int status = battery.Status;
		bool flag = (((uint)(status - 1) <= 1u || status == 4) ? true : false);
		MainViewModel mainViewModel = this;
		SolidColorBrush batteryBrush;
		if (flag)
		{
			batteryBrush = AccentBrush;
		}
		else if (battery.Percentage <= 20)
		{
			batteryBrush = DangerBrush;
		}
		else
		{
			batteryBrush = ((battery.Percentage <= 40) ? WarningBrush : SuccessBrush);
		}
		mainViewModel.BatteryBrush = batteryBrush;
	}

	private static string BuildDetails(DeviceSnapshot snapshot)
	{
		string text = snapshot.Info.PortName;
		if (text.Contains("mi_02&col02", StringComparison.OrdinalIgnoreCase))
		{
			text = "MI_02 · COL02";
		}
		else if (text.Contains("mi_02&col01", StringComparison.OrdinalIgnoreCase))
		{
			text = "MI_02 · COL01";
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(FormatDeviceName(snapshot.Info.Name));
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(24, 2, stringBuilder2);
		handler.AppendLiteral("PID 0x");
		handler.AppendFormatted(snapshot.Info.ProductId, "X4");
		handler.AppendLiteral(" · device index 0x");
		handler.AppendFormatted(snapshot.DeviceIndex, "X2");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(3, 2, stringBuilder2);
		handler.AppendFormatted(snapshot.Info.ProtocolText);
		handler.AppendLiteral(" · ");
		handler.AppendFormatted(snapshot.Info.ConnectionText);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder.AppendLine(snapshot.OnboardMode == 2 ? "模式 HOST（实时设置）" : $"模式 ONBOARD（{snapshot.OnboardMode}）");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 2, stringBuilder2);
		handler.AppendFormatted(snapshot.Descriptor.Count);
		handler.AppendLiteral(" 个板载配置 · ");
		handler.AppendFormatted(snapshot.Descriptor.Size);
		handler.AppendLiteral(" B/sector");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder2);
		handler.AppendLiteral("接口 ");
		handler.AppendFormatted(text);
		stringBuilder6.AppendLine(ref handler);
		return stringBuilder.ToString().TrimEnd();
	}

	private static string FormatDeviceName(string name)
	{
		if (name.Contains("PRO X", StringComparison.OrdinalIgnoreCase))
		{
			return "PRO X SUPERLIGHT";
		}
		return name.ToUpperInvariant();
	}

	private void LoadSelectedProfile()
	{
		if (SelectedProfile != null)
		{
			_suppress = true;
			DpiSlots.Clear();
			int[] dpiSlots = SelectedProfile.DpiSlots;
			foreach (int value in dpiSlots)
			{
				DpiSlots.Add(new DpiSlotVm(value));
			}
			_selectedDpiSlotIndex = Math.Clamp(SelectedProfile.DefaultDpiIndex, 0, DpiSlots.Count - 1);
			OnPropertyChanged("SelectedDpiSlotIndex");
			OnPropertyChanged("SelectedDpiSlotNumber");
			_dpi = DpiSlots[_selectedDpiSlotIndex].Value;
			OnPropertyChanged("Dpi");
			_profileName = SelectedProfile.Name;
			OnPropertyChanged("ProfileName");
			_selectedReportRate = ReportRateOptions.FirstOrDefault((ReportRateVm r) => r.Ms == SelectedProfile.ReportRateMs) ?? ReportRateOptions.FirstOrDefault();
			OnPropertyChanged("SelectedReportRate");
			_suppress = false;
			IsDirty = false;
			UpdateDpiSelection();
			UpdateRateSelection();
		}
	}

	public void SelectDpiSlot(DpiSlotVm slot)
	{
		int index = DpiSlots.IndexOf(slot);
		if (index < 0)
		{
			return;
		}

		if (index == SelectedDpiSlotIndex)
		{
			Dpi = slot.Value;
			OnEditableChanged(immediate: true);
		}
		else
		{
			SelectedDpiSlotIndex = index;
		}

		UpdateDpiSelection();
	}

	public void SelectReportRate(ReportRateVm rate)
	{
		if (ReferenceEquals(SelectedReportRate, rate))
		{
			OnEditableChanged(immediate: true);
			return;
		}

		SelectedReportRate = rate;
		UpdateRateSelection();
	}

	private void UpdateDpiSelection()
	{
		for (int i = 0; i < DpiSlots.Count; i++)
		{
			DpiSlots[i].IsSelected = i == SelectedDpiSlotIndex;
		}
	}

	private void UpdateRateSelection()
	{
		foreach (ReportRateVm rate in ReportRateOptions)
		{
			rate.IsSelected = ReferenceEquals(rate, SelectedReportRate);
		}
	}

	private async Task SaveSelectedProfileAsync()
	{
		if (!IsDirty || SelectedProfile == null)
		{
			return;
		}
		_autoSavePending = false;
		EditSnapshot editSnapshot = CaptureEditSnapshot();
		if ((object)editSnapshot == null)
		{
			return;
		}
		SetBusy(busy: true, "正在保存…");
		try
		{
			await SaveSnapshotAsync(editSnapshot, auto: false);
		}
		finally
		{
			SetBusy(busy: false);
		}
	}

	private void OnEditableChanged(bool immediate)
	{
		if (!_suppress && IsConnected)
		{
			IsDirty = true;
			RequestAutoApply(immediate);
			RequestAutoSave();
		}
	}

	private EditSnapshot? CaptureEditSnapshot(ProfileVm? profile = null)
	{
		if (profile == null)
		{
			profile = SelectedProfile;
		}
		if (profile == null)
		{
			return null;
		}
		return new EditSnapshot(profile.Index, profile.IsEnabled, Dpi, SelectedReportRate?.Ms ?? 1, ProfileName, DpiSlots.Select((DpiSlotVm s) => s.Value).ToArray(), SelectedDpiSlotIndex);
	}

	private void RequestAutoApply(bool immediate)
	{
		_autoApplyPending = true;
		_autoApplyImmediate |= immediate;
		if (!_autoApplyRunning)
		{
			AutoApplyLoopAsync();
		}
	}

	private async Task AutoApplyLoopAsync()
	{
		_autoApplyRunning = true;
		try
		{
			while (_autoApplyPending)
			{
				bool autoApplyImmediate = _autoApplyImmediate;
				_autoApplyImmediate = false;
				await Task.Delay(autoApplyImmediate ? 1 : 120);
				if (_autoApplyPending)
				{
					_autoApplyPending = false;
					EditSnapshot editSnapshot = CaptureEditSnapshot();
					if ((object)editSnapshot == null)
					{
						break;
					}
					await ApplyLiveAsync(editSnapshot);
					continue;
				}
				break;
			}
		}
		catch (Exception ex)
		{
			StatusText = "应用失败: " + ex.Message;
		}
		finally
		{
			_autoApplyRunning = false;
			if (_autoApplyPending)
			{
				AutoApplyLoopAsync();
			}
		}
	}

	private async Task ApplyLiveAsync(EditSnapshot snapshot)
	{
		if (_device == null || snapshot.Index != _activeProfile)
		{
			return;
		}
		await _deviceGate.WaitAsync();
		try
		{
			HidppDevice device = _device;
			if (device != null)
			{
				await Task.Run(() =>
				{
					device.EnsureHostMode();
					device.ApplyLive((ushort)snapshot.Dpi, (byte)snapshot.RateMs);
				});
				StatusText = "参数已应用";
			}
		}
		catch (Exception ex)
		{
			StatusText = "应用失败: " + ex.Message;
		}
		finally
		{
			_deviceGate.Release();
		}
	}

	private void RequestAutoSave()
	{
		_autoSavePending = true;
		if (!_autoSaveRunning)
		{
			AutoSaveLoopAsync();
		}
	}

	private async Task AutoSaveLoopAsync()
	{
		_autoSaveRunning = true;
		try
		{
			while (_autoSavePending)
			{
				await Task.Delay(650);
				if (_autoSavePending)
				{
					_autoSavePending = false;
					EditSnapshot editSnapshot = CaptureEditSnapshot();
					if ((object)editSnapshot == null)
					{
						break;
					}
					await SaveSnapshotAsync(editSnapshot, auto: true);
					continue;
				}
				break;
			}
		}
		catch (Exception ex)
		{
			StatusText = "自动保存失败: " + ex.Message;
		}
		finally
		{
			_autoSaveRunning = false;
			if (_autoSavePending)
			{
				AutoSaveLoopAsync();
			}
		}
	}

	private async Task SaveSnapshotAsync(EditSnapshot snapshot, bool auto)
	{
		if (_device == null)
		{
			return;
		}

		string? backupPath = null;
		HardwareSyncResult sync;
		await _deviceGate.WaitAsync();
		try
		{
			HidppDevice device = _device;
			sync = await Task.Run(() =>
			{
				backupPath = EnsureSessionBackup();
				device.EnsureHostMode();
				bool active = snapshot.Index == _activeProfile;
				if (active)
				{
					device.ApplyLive((ushort)snapshot.Dpi, (byte)snapshot.RateMs);
					Thread.Sleep(80);
				}

				if (snapshot.Index > 1 && !snapshot.Enabled)
				{
					device.EnableProfiles(5);
				}

				OnboardProfile profile = device.ReadProfile(snapshot.Index);
				profile.Name = snapshot.Name;
				profile.ReportRateMs = (byte)snapshot.RateMs;
				profile.SetDpiSlots(snapshot.Slots, snapshot.DefaultIndex);
				device.WriteProfile(profile);

				if (active)
				{
					// Writing the profile can make the firmware reload its own DPI state.
					// Re-apply live and then read the real hardware state back.
					device.ApplyLive((ushort)snapshot.Dpi, (byte)snapshot.RateMs);
					Thread.Sleep(120);
				}

				OnboardProfile verify = device.ReadProfile(snapshot.Index);
				bool profileOk = verify.CrcValid &&
				                 verify.ReportRateMs == snapshot.RateMs &&
				                 verify.DefaultDpiIndex == snapshot.DefaultIndex &&
				                 verify.Name == snapshot.Name &&
				                 verify.DpiSlots.SequenceEqual(snapshot.Slots);

				int actualDpi = active ? device.ReadDpi() : 0;
				int actualRate = active ? device.ReadReportRateMs() : 0;
				bool liveOk = !active || (actualDpi == snapshot.Dpi && actualRate == snapshot.RateMs);

				return new HardwareSyncResult(profileOk && liveOk, actualDpi, actualRate, profileOk);
			});

			if (backupPath != null)
			{
				BackupStatus = "写入前备份: " + Path.GetFileName(backupPath);
			}

			if (sync.Success)
			{
				if (SelectedProfile?.Index == snapshot.Index && IsSnapshotCurrent(snapshot))
				{
					SelectedProfile.Apply(snapshot.Slots, snapshot.DefaultIndex, snapshot.RateMs, snapshot.Name, enabled: true);
					IsDirty = false;
				}

				StatusText = auto ? $"P{snapshot.Index} 已自动保存" : $"P{snapshot.Index} 已保存到鼠标";
				ShowToast("修改成功", success: true);
			}
			else
			{
				int actualHz = sync.ActualRateMs > 0 ? 1000 / sync.ActualRateMs : 0;
				StatusText = $"同步失败：期望 {snapshot.Dpi} DPI / {1000 / Math.Max(1, snapshot.RateMs)} Hz，硬件回读 {sync.ActualDpi} DPI / {actualHz} Hz";
				ShowToast("修改失败：硬件回读不一致", success: false);
			}

			RaiseCommands();
		}
		catch (Exception ex)
		{
			StatusText = "保存失败: " + ex.Message;
			ShowToast("修改失败: " + ex.Message, success: false);
		}
		finally
		{
			_deviceGate.Release();
		}
	}

	private bool IsSnapshotCurrent(EditSnapshot snapshot)
	{
		if (snapshot.Dpi == Dpi && snapshot.RateMs == (SelectedReportRate?.Ms ?? 1) && snapshot.DefaultIndex == SelectedDpiSlotIndex && snapshot.Name == ProfileName)
		{
			return snapshot.Slots.SequenceEqual(DpiSlots.Select((DpiSlotVm s) => s.Value));
		}
		return false;
	}

	private async Task EnableProfilesAsync()
	{
		if (_device == null)
		{
			return;
		}
		await _deviceGate.WaitAsync();
		try
		{
			SetBusy(busy: true, "正在启用 5 个板载配置…");
			string backupPath = EnsureSessionBackup();
			await Task.Run(() =>
			{
				_device.EnableProfiles(5);
			});
			foreach (ProfileVm profile in Profiles)
			{
				profile.IsEnabled = true;
			}
			if (backupPath != null)
			{
				BackupStatus = "写入前备份: " + Path.GetFileName(backupPath);
			}
			OnPropertyChanged("ShowEnableProfiles");
			OnPropertyChanged("EnableProfilesHint");
			StatusText = "P1–P5 已全部启用";
		}
		catch (Exception ex)
		{
			StatusText = "启用失败: " + ex.Message;
		}
		finally
		{
			SetBusy(busy: false);
			_deviceGate.Release();
		}
	}

	private async Task AddCurrentAppAsync(ProfileVm? profile)
	{
		string foregroundProcessName = ForegroundWatcher.GetForegroundProcessName();
		if (string.IsNullOrWhiteSpace(foregroundProcessName))
		{
			StatusText = "无法读取前台应用";
			return;
		}
		string b = Process.GetCurrentProcess().ProcessName + ".exe";
		if (string.Equals(foregroundProcessName, b, StringComparison.OrdinalIgnoreCase))
		{
			StatusText = "请先切换到目标应用，再点添加";
			return;
		}
		int profile2 = profile?.Index ?? SelectedProfile?.Index ?? 1;
		AddRule(foregroundProcessName, profile2);
		if (AutoSwitch)
		{
			await ApplyForProcessAsync(foregroundProcessName, force: true);
		}
	}

	private void AddRule(string processName, int profile)
	{
		processName = processName.Trim();
		if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			processName += ".exe";
		}
		RuleVm ruleVm = Rules.FirstOrDefault((RuleVm r) => string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
		if (ruleVm != null)
		{
			ruleVm.Profile = Math.Clamp(profile, 1, 5);
			StatusText = $"已更新 {processName} → P{ruleVm.Profile}";
		}
		else
		{
			Rules.Add(new RuleVm(new AppRule
			{
				ProcessName = processName,
				DisplayName = Path.GetFileNameWithoutExtension(processName),
				Profile = Math.Clamp(profile, 1, 5)
			}, Profiles));
			StatusText = $"已添加 {processName} → P{Math.Clamp(profile, 1, 5)}";
		}
		PersistRules();
		UpdateAssignedCounts();
	}

	private void PersistRules()
	{
		_config.Rules = Rules.Select((RuleVm r) => r.ToModel()).ToList();
		_store.Save(_config);
		OnPropertyChanged("HasNoRules");
		OnPropertyChanged("HasRules");
		OnPropertyChanged("RulesCountText");
	}

	private void UpdateAssignedCounts()
	{
		foreach (ProfileVm profile in Profiles)
		{
			profile.AssignedCount = Rules.Count((RuleVm r) => r.Profile == profile.Index);
		}
	}

	private void LoadRules()
	{
		Rules.Clear();
		foreach (AppRule rule in _config.Rules)
		{
			Rules.Add(new RuleVm(rule, Profiles));
		}
		UpdateAssignedCounts();
		OnPropertyChanged("HasNoRules");
		OnPropertyChanged("HasRules");
		OnPropertyChanged("RulesCountText");
	}

	private void OnForegroundProcessChanged(string processName)
	{
		_dispatcher.BeginInvoke((Delegate)(Action)(() =>
		{
			ApplyForProcessAsync(processName, force: false);
		}), Array.Empty<object>());
	}

	private async Task ApplyForProcessAsync(string? processName, bool force)
	{
		if (!AutoSwitch || _device == null || !IsConnected || string.IsNullOrWhiteSpace(processName))
		{
			return;
		}
		string b = Process.GetCurrentProcess().ProcessName + ".exe";
		if (string.Equals(processName, b, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		RuleVm rule = Rules.FirstOrDefault((RuleVm r) => string.Equals(r.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
		int target = Math.Clamp(rule?.Profile ?? _config.DefaultProfile, 1, 5);
		if ((!force && target == _activeProfile) || !(await _switchGate.WaitAsync(0)))
		{
			return;
		}
		try
		{
			await _deviceGate.WaitAsync();
			try
			{
				await Task.Run(() =>
				{
					_device.ApplyProfile(target);
				});
				_activeProfile = target;
				foreach (ProfileVm profile in Profiles)
				{
					profile.IsActive = profile.Index == target;
				}
				OnPropertyChanged("ActiveProfileLabel");
				OnPropertyChanged("ActiveProfileName");
				StatusText = ((rule == null) ? "已应用默认配置" : $"已切换到 {rule.ProcessName} → P{target}");
			}
			finally
			{
				_deviceGate.Release();
			}
		}
		catch (Exception ex)
		{
			StatusText = "切换配置失败: " + ex.Message;
		}
		finally
		{
			_switchGate.Release();
		}
	}

	private async Task BackupToFileAsync()
	{
		if (_device == null)
		{
			return;
		}
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "备份板载配置",
			Filter = "GhubLite backup (*.json)|*.json",
			InitialDirectory = _store.BackupsDirectory,
			FileName = $"ghublite-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
		};
		if (dialog.ShowDialog() != true)
		{
			return;
		}
		await _deviceGate.WaitAsync();
		try
		{
			SetBusy(busy: true, "正在备份…");
			BackupData backup = await Task.Run(() => _backups.Capture(_device));
			await Task.Run(() =>
			{
				File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(backup, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			});
			BackupStatus = "已备份: " + Path.GetFileName(dialog.FileName);
			StatusText = "备份完成";
		}
		catch (Exception ex)
		{
			StatusText = "备份失败: " + ex.Message;
		}
		finally
		{
			SetBusy(busy: false);
			_deviceGate.Release();
		}
	}

	private async Task RestoreFromFileAsync()
	{
		if (_device == null)
		{
			return;
		}
		OpenFileDialog dialog = new OpenFileDialog
		{
			Title = "恢复板载配置",
			Filter = "GhubLite backup (*.json)|*.json",
			InitialDirectory = _store.BackupsDirectory
		};
		if (dialog.ShowDialog() != true || MessageBox.Show("恢复会用备份覆盖 5 个板载 Profile，确定继续？", "GhubLite", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		await _deviceGate.WaitAsync();
		try
		{
			SetBusy(busy: true, "正在恢复…");
			string backupPath = EnsureSessionBackup();
			BackupData backup = _backups.Load(dialog.FileName);
			await Task.Run(() =>
			{
				_backups.Restore(_device, backup);
			});
			if (backupPath != null)
			{
				BackupStatus = "恢复前备份: " + Path.GetFileName(backupPath);
			}
			StatusText = "恢复完成";
		}
		catch (Exception ex)
		{
			StatusText = "恢复失败: " + ex.Message;
		}
		finally
		{
			SetBusy(busy: false);
			_deviceGate.Release();
		}
		await ConnectAndRefreshAsync();
	}

	public async Task SwitchProfileAsync(int index)
	{
		bool flag = _device == null;
		if (!flag)
		{
			bool flag2 = ((index < 1 || index > 5) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return;
		}
		await _deviceGate.WaitAsync();
		try
		{
			SetBusy(busy: true, $"正在切换到 P{index}…");
			await Task.Run(() =>
			{
				_device.ApplyProfile(index);
			});
			_activeProfile = index;
			foreach (ProfileVm profile in Profiles)
			{
				profile.IsActive = profile.Index == index;
			}
			OnPropertyChanged("ActiveProfileLabel");
			OnPropertyChanged("ActiveProfileName");
			StatusText = $"已切换到 P{index}";
		}
		catch (Exception ex)
		{
			StatusText = $"切换 P{index} 失败: {ex.Message}";
		}
		finally
		{
			SetBusy(busy: false);
			_deviceGate.Release();
		}
	}

	private async Task ReconnectAsync()
	{
		await _deviceGate.WaitAsync();
		try
		{
			_device?.Dispose();
			_device = null;
			IsConnected = false;
		}
		finally
		{
			_deviceGate.Release();
		}
		await ConnectAndRefreshAsync();
	}

	private async Task RefreshAsync()
	{
		if (_device == null)
		{
			return;
		}
		await _deviceGate.WaitAsync();
		try
		{
			SetBusy(busy: true, "正在刷新…");
			ApplySnapshot(await Task.Run(() => ReadSnapshot(_device)));
			StatusText = "已刷新";
		}
		catch (Exception ex)
		{
			StatusText = "刷新失败: " + ex.Message;
		}
		finally
		{
			SetBusy(busy: false);
			_deviceGate.Release();
		}
	}

	private string? EnsureSessionBackup()
	{
		if (_device == null)
		{
			return null;
		}
		if (_sessionBackupDone)
		{
			return _sessionBackupPath;
		}
		BackupData backup = _backups.Capture(_device);
		_sessionBackupPath = _backups.Save(backup);
		_sessionBackupDone = true;
		return _sessionBackupPath;
	}

	private async Task BatteryLoopAsync()
	{
		_ = 2;
		try
		{
			while (!_cts.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromSeconds(60L), _cts.Token);
				if (!IsConnected || _device == null)
				{
					continue;
				}
				await _deviceGate.WaitAsync(_cts.Token);
				try
				{
					HidppDevice device = _device;
					if (device != null)
					{
						BatteryInfo battery = await Task.Run(() => device.ReadBattery(), _cts.Token);
						_dispatcher.BeginInvoke((Delegate)(Action)(() =>
						{
							ApplyBattery(battery);
						}), Array.Empty<object>());
					}
				}
				catch
				{
				}
				finally
				{
					_deviceGate.Release();
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void Disconnect(string status)
	{
		_device?.Dispose();
		_device = null;
		IsConnected = false;
		ConnectionText = "未连接";
		StatusText = status;
		BatteryText = "--";
		BatteryStatusText = "未连接";
		BatteryLevelText = "";
		RaiseCommands();
	}

	private void SetBusy(bool busy, string? status = null)
	{
		IsBusy = busy;
		if (!string.IsNullOrWhiteSpace(status))
		{
			StatusText = status;
		}
		RaiseCommands();
	}

	private void RaiseCommands()
	{
		SaveProfileCommand.RaiseCanExecuteChanged();
		EnableProfilesCommand.RaiseCanExecuteChanged();
		AddCurrentAppCommand.RaiseCanExecuteChanged();
		AssignCurrentAppCommand.RaiseCanExecuteChanged();
		BackupCommand.RaiseCanExecuteChanged();
		RestoreCommand.RaiseCanExecuteChanged();
		ReconnectCommand.RaiseCanExecuteChanged();
		RefreshCommand.RaiseCanExecuteChanged();
	}

	private async void ShowToast(string text, bool success)
	{
		_toastCts?.Cancel();
		var cts = new CancellationTokenSource();
		_toastCts = cts;
		ToastText = text;
		ToastBrush = success ? SuccessBrush : DangerBrush;
		ToastVisible = true;
		try
		{
			await Task.Delay(2400, cts.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (!cts.IsCancellationRequested)
		{
			ToastVisible = false;
		}
	}

	public async Task ShutdownAsync()
	{
		try
		{
			if (IsDirty && _device != null)
			{
				_autoSavePending = false;
				EditSnapshot? snapshot = CaptureEditSnapshot();
				if (snapshot != null)
				{
					await SaveSnapshotAsync(snapshot, auto: true);
				}
			}

			HidppDevice? device = _device;
			if (device != null)
			{
				await Task.Run(() => device.SetOnboardMode(1));
			}
		}
		catch
		{
		}
		Dispose();
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_toastCts?.Cancel();
		try
		{
			_device?.SetOnboardMode(1);
		}
		catch
		{
		}

		_cts.Cancel();
		_watcher.Dispose();
		_device?.Dispose();
		_cts.Dispose();
		_deviceGate.Dispose();
		_switchGate.Dispose();
	}

	private static SolidColorBrush Frozen(string hex)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		((Freezable)solidColorBrush).Freeze();
		return solidColorBrush;
	}
}
