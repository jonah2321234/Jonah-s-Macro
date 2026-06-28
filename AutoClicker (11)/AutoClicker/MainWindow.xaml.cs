using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoClicker
{
    public partial class MainWindow : Window
    {
        private bool _isRunning = false;
        private CancellationTokenSource? _cts;
        private HwndSource? _hwndSource;
        private int _loopCount = 0;

        private AppSettings _settings = new();
        private bool _suppressSettingsEvents = true; // avoid re-saving while we're populating controls on load

        private readonly Stopwatch _uptimeWatch = new();
        private DispatcherTimer? _uptimeTimer;
        private DispatcherTimer? _saveDebounceTimer;

        // The full key sequence: 1-9 then 0, matching a real keyboard's top row order.
        private static readonly int[] Sequence = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 0 };

        public MainWindow()
        {
            InitializeComponent();

            // Ask Windows for 1ms timer resolution instead of the default ~15ms.
            // Without this, Task.Delay on small values can drift significantly,
            // making low-delay settings feel inconsistent or "stuttery".
            NativeMethods.TimeBeginPeriod(1);

            _settings = AppSettings.Load();
            LoadSettingsIntoUi();
            _suppressSettingsEvents = false;

            StartUptimeClock();
        }

        private void LoadSettingsIntoUi()
        {
            _suppressSettingsEvents = true;

            DelaySlider.Value = _settings.DelayMs;
            DelayExactTextBox.Text = FormatDelay(_settings.DelayMs);
            ResyncCheckBox.IsChecked = _settings.ResyncEnabled;
            ResyncMinutesTextBox.Text = _settings.ResyncIntervalMinutes.ToString();

            foreach (ComboBoxItem item in HotkeyComboBox.Items)
            {
                if (item.Tag is string tagStr && uint.TryParse(tagStr, out uint vk) && vk == _settings.HotkeyVk)
                {
                    HotkeyComboBox.SelectedItem = item;
                    break;
                }
            }
            if (HotkeyComboBox.SelectedItem == null && HotkeyComboBox.Items.Count > 0)
                HotkeyComboBox.SelectedIndex = 0;

            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Content?.ToString() == _settings.Theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
            if (ThemeComboBox.SelectedItem == null && ThemeComboBox.Items.Count > 0)
                ThemeComboBox.SelectedIndex = 0;

            ThemeManager.Apply(_settings.Theme);
        }

        private static string FormatDelay(double ms) => ms % 1 == 0 ? ms.ToString("0") : ms.ToString("0.#");

        // Writing to disk on every single slider tick (which fires dozens of times per
        // second while dragging) can cause tiny hitches, especially while the click loop
        // is also running. Instead, wait for things to settle for a moment before saving.
        private void ScheduleSave()
        {
            if (_suppressSettingsEvents) return;

            _saveDebounceTimer?.Stop();
            _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveDebounceTimer.Tick += (_, _) =>
            {
                _saveDebounceTimer?.Stop();
                _settings.Save();
            };
            _saveDebounceTimer.Start();
        }

        private void StartUptimeClock()
        {
            _uptimeWatch.Start();
            _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uptimeTimer.Tick += (_, _) =>
            {
                var t = _uptimeWatch.Elapsed;
                UptimeText.Text = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
            };
            _uptimeTimer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _hwndSource?.AddHook(WndProc);
            RegisterToggleHotkey();
        }

        protected override void OnClosed(EventArgs e)
        {
            UnregisterToggleHotkey();
            _hwndSource?.RemoveHook(WndProc);
            _uptimeTimer?.Stop();
            StopLoop();
            _settings.Save();
            NativeMethods.TimeEndPeriod(1);
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == NativeMethods.ToggleHotkeyId)
            {
                ToggleRunning();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void RegisterToggleHotkey()
        {
            if (_hwndSource == null) return;
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, NativeMethods.ToggleHotkeyId);
            bool ok = NativeMethods.RegisterHotKey(_hwndSource.Handle, NativeMethods.ToggleHotkeyId, NativeMethods.MOD_NONE, _settings.HotkeyVk);
            StatusSubText.Text = ok
                ? "Press the hotkey (see Settings) or the button to start"
                : "Hotkey unavailable — another app may be using it. Pick a different one in Settings.";
        }

        private void UnregisterToggleHotkey()
        {
            if (_hwndSource == null) return;
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, NativeMethods.ToggleHotkeyId);
        }

        private void HotkeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HotkeyComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tagStr && uint.TryParse(tagStr, out uint vk))
            {
                _settings.HotkeyVk = vk;
                RegisterToggleHotkey();
                if (!_suppressSettingsEvents) ScheduleSave();
            }
        }

        private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _settings.DelayMs = DelaySlider.Value;
            if (!_suppressSettingsEvents)
            {
                DelayExactTextBox.Text = FormatDelay(DelaySlider.Value);
                ScheduleSave();
            }
        }

        private void DelayExactTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSettingsEvents) return;
            if (double.TryParse(DelayExactTextBox.Text, out double ms) && ms >= 0)
            {
                _settings.DelayMs = ms;
                // Keep the slider in sync without re-triggering this handler in a loop
                _suppressSettingsEvents = true;
                DelaySlider.Value = Math.Min(ms, DelaySlider.Maximum);
                _suppressSettingsEvents = false;
                ScheduleSave();
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Content is string themeName)
            {
                _settings.Theme = themeName;
                ThemeManager.Apply(themeName);
                if (!_suppressSettingsEvents) ScheduleSave();
            }
        }

        private void DecimalOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            var box = sender as TextBox;
            foreach (char c in e.Text)
            {
                bool isDigit = char.IsDigit(c);
                bool isDot = c == '.' && box != null && !box.Text.Contains('.');
                if (!isDigit && !isDot)
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void SettingsControl_Changed(object sender, RoutedEventArgs e) => SaveCurrentSettings();

        private void ResyncMinutesTextBox_TextChanged(object sender, TextChangedEventArgs e) => SaveCurrentSettings();

        private void SaveCurrentSettings()
        {
            _settings.ResyncEnabled = ResyncCheckBox.IsChecked == true;
            if (int.TryParse(ResyncMinutesTextBox.Text, out int minutes) && minutes > 0)
                _settings.ResyncIntervalMinutes = minutes;

            if (!_suppressSettingsEvents) ScheduleSave();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e) => ToggleRunning();

        private void ToggleRunning()
        {
            if (_isRunning) StopLoop();
            else StartLoop();
        }

        private void StartLoop()
        {
            if (_isRunning) return;

            double delayMs = Math.Max(0, _settings.DelayMs);
            bool resyncEnabled = _settings.ResyncEnabled;
            int resyncMinutes = Math.Max(1, _settings.ResyncIntervalMinutes);

            _isRunning = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            SetRunningUi(true);

            Task.Run(async () =>
            {
                var resyncWatch = Stopwatch.StartNew();
                var delay = TimeSpan.FromMilliseconds(delayMs);
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        foreach (int digit in Sequence)
                        {
                            if (token.IsCancellationRequested) break;

                            NativeMethods.SendKeyPress(NativeMethods.VkForDigit(digit));
                            if (delayMs > 0) await Task.Delay(delay, token);

                            NativeMethods.SendLeftClick();
                            if (delayMs > 0) await Task.Delay(delay, token);
                        }

                        _loopCount++;
                        Dispatcher.Invoke(() => LoopCountText.Text = _loopCount.ToString());

                        // /swords delay: pause and send the //swords chat command, then resume
                        if (resyncEnabled && resyncWatch.Elapsed.TotalMinutes >= resyncMinutes && !token.IsCancellationRequested)
                        {
                            await Task.Delay(300, token);
                            NativeMethods.SendSwordsCommand();
                            await Task.Delay(300, token);
                            resyncWatch.Restart();
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // expected when stopping
                }
                catch (Exception)
                {
                    // Something unexpected happened mid-loop (e.g. a transient input API
                    // failure). Rather than leave the UI stuck showing "RUNNING" while
                    // nothing is actually happening, fall back to a clean stopped state.
                    _isRunning = false;
                    SetRunningUi(false);
                }
            }, token);
        }

        private void StopLoop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            SetRunningUi(false);
        }

        private void SetRunningUi(bool running)
        {
            Dispatcher.Invoke(() =>
            {
                if (running)
                {
                    StatusText.Text = "RUNNING";
                    StatusText.Foreground = (Brush)FindResource("AccentBrush");
                    StatusDot.Fill = (Brush)FindResource("AccentBrush");
                    StatusSubText.Text = "Looping 1 → 9 → 0, click after each key";
                    ToggleButton.Content = "Stop";
                }
                else
                {
                    StatusText.Text = "OFF";
                    StatusText.Foreground = (Brush)FindResource("TextPrimary");
                    StatusDot.Fill = (Brush)FindResource("DangerBrush");
                    StatusSubText.Text = "Press the hotkey or the button to start";
                    ToggleButton.Content = "Start";
                }
            });
        }

        private void NumericOnly_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }
    }
}
