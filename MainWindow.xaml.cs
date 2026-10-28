using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Button = System.Windows.Controls.Button;
using WindowShield.Models;
using WindowShield.Native;
using WindowShield.Services;
using WindowShield.Views;

namespace WindowShield
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private OverlayManager _overlayManager;
        private HotkeyManager? _hotkeyManager;

        private readonly ObservableCollection<TargetWindowInfo> _allWindows = new ObservableCollection<TargetWindowInfo>();
        private readonly ObservableCollection<TargetWindowInfo> _filteredWindows = new ObservableCollection<TargetWindowInfo>();

        public MainWindow()
        {
            InitializeComponent();

            _settings = SettingsService.Load();
            _overlayManager = new OverlayManager(_settings);
            _overlayManager.ActiveOverlaysChanged += OverlayManager_ActiveOverlaysChanged;

            AppsListView.ItemsSource = _filteredWindows;

            // Apply settings to UI controls
            SliderOpacity.Value = _settings.OverlayOpacity;
            TxtOpacityVal.Text = $"{(int)(_settings.OverlayOpacity * 100)}%";
            TxtCustomLabel.Text = _settings.CustomText;
            SliderTitleBar.Value = _settings.TitleBarHeight;
            TxtTitleBarVal.Text = $"{(int)_settings.TitleBarHeight} px";
            ChkHideForPresenter.IsChecked = _settings.HideOverlayForPresenter;
            ChkExposeTitleBar.IsChecked = _settings.ExposeTitleBar;
            ChkShowText.IsChecked = _settings.ShowCustomText;
            ChkPassThrough.IsChecked = _settings.IsClickThrough;

            Loaded += MainWindow_Loaded;
            Unloaded += MainWindow_Unloaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            _hotkeyManager = new HotkeyManager(hwnd);
            _hotkeyManager.RegisterGlobalHotkeys();
            _hotkeyManager.HotkeyPressed += HotkeyManager_HotkeyPressed;

            RefreshWindowsList();
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            _hotkeyManager?.Dispose();
            _overlayManager.Dispose();
            SettingsService.Save(_settings);
        }

        private void HotkeyManager_HotkeyPressed(int hotkeyId)
        {
            if (hotkeyId == HotkeyManager.HOTKEY_TOGGLE_ALL)
            {
                BtnUncoverAll_Click(this, new RoutedEventArgs());
            }
            else if (hotkeyId == HotkeyManager.HOTKEY_TOGGLE_PICKER)
            {
                BtnLaunchPicker_Click(this, new RoutedEventArgs());
            }
        }

        private void RefreshWindowsList()
        {
            IntPtr selfHwnd = new WindowInteropHelper(this).Handle;
            var list = WindowEnumerator.GetVisibleWindows(selfHwnd);

            _allWindows.Clear();
            foreach (var win in list)
            {
                win.IsCovered = _overlayManager.IsCovered(win.Handle);
                _allWindows.Add(win);
            }

            ApplyFilter();
            TxtFooterCount.Text = $"Total Active Apps: {_allWindows.Count}";
        }

        private void ApplyFilter()
        {
            string query = TxtSearch.Text.Trim().ToLowerInvariant();
            _filteredWindows.Clear();

            foreach (var win in _allWindows)
            {
                if (string.IsNullOrEmpty(query) ||
                    win.Title.ToLowerInvariant().Contains(query) ||
                    win.ProcessName.ToLowerInvariant().Contains(query))
                {
                    _filteredWindows.Add(win);
                }
            }
        }

        private void OverlayManager_ActiveOverlaysChanged()
        {
            int count = _overlayManager.CoveredCount;
            TxtStatus.Text = $"{count} Apps Covered";

            if (count > 0)
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2000E5FF"));
                StatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
                StatusPulse.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
                TxtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
            }
            else
            {
                StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#208A94A6"));
                StatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#364052"));
                StatusPulse.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A94A6"));
                TxtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A94A6"));
            }

            foreach (var win in _allWindows)
            {
                win.IsCovered = _overlayManager.IsCovered(win.Handle);
            }
        }

        private void BtnToggleOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TargetWindowInfo target)
            {
                _overlayManager.ToggleOverlay(target);
                target.IsCovered = _overlayManager.IsCovered(target.Handle);
            }
        }

        private void BtnToggleStealth_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is TargetWindowInfo target)
            {
                if (target.IsStealthMode)
                {
                    DisplayAffinityManager.DisableStealthMode(target.Handle, target.ProcessId);
                    target.IsStealthMode = false;
                }
                else
                {
                    DisplayAffinityManager.EnableStealthMode(target.Handle, target.ProcessId);
                    target.IsStealthMode = true;
                }
            }
        }

        private void BtnLaunchPicker_Click(object sender, RoutedEventArgs e)
        {
            var picker = new WindowPickerOverlay();
            picker.WindowSelected += (hwnd) =>
            {
                var target = _allWindows.FirstOrDefault(w => w.Handle == hwnd);
                if (target == null)
                {
                    RefreshWindowsList();
                    target = _allWindows.FirstOrDefault(w => w.Handle == hwnd);
                }

                if (target != null)
                {
                    _overlayManager.AddOverlay(target);
                }
            };
            picker.Show();
        }

        private void BtnRefreshList_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindowsList();
        }

        private void BtnUncoverAll_Click(object sender, RoutedEventArgs e)
        {
            _overlayManager.RemoveAllOverlays();
            foreach (var win in _allWindows)
            {
                if (win.IsStealthMode)
                {
                    DisplayAffinityManager.DisableStealthMode(win.Handle, win.ProcessId);
                    win.IsStealthMode = false;
                }
            }
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            ColSettings.Width = ColSettings.Width.Value == 0 ? new GridLength(320) : new GridLength(0);
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            ColSettings.Width = new GridLength(0);
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtOpacityVal != null)
            {
                _settings.OverlayOpacity = e.NewValue;
                TxtOpacityVal.Text = $"{(int)(e.NewValue * 100)}%";
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void BtnPresetColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                _settings.OverlayColorHex = hex;
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void TxtCustomLabel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settings != null)
            {
                _settings.CustomText = TxtCustomLabel.Text;
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void SliderTitleBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtTitleBarVal != null)
            {
                _settings.TitleBarHeight = e.NewValue;
                TxtTitleBarVal.Text = $"{(int)e.NewValue} px";
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void ChkHideForPresenter_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings != null && ChkHideForPresenter != null)
            {
                _settings.HideOverlayForPresenter = ChkHideForPresenter.IsChecked ?? false;
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void ChkExposeTitleBar_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings != null && ChkExposeTitleBar != null)
            {
                _settings.ExposeTitleBar = ChkExposeTitleBar.IsChecked ?? true;
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void ChkShowText_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings != null && ChkShowText != null)
            {
                _settings.ShowCustomText = ChkShowText.IsChecked ?? true;
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void ChkPassThrough_Changed(object sender, RoutedEventArgs e)
        {
            if (_settings != null && ChkPassThrough != null)
            {
                _settings.IsClickThrough = ChkPassThrough.IsChecked ?? false;
                _overlayManager?.UpdateSettings(_settings);
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            SearchPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtSearch.Text))
            {
                SearchPlaceholder.Visibility = Visibility.Visible;
            }
        }
    }
}