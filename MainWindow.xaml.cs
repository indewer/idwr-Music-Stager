using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;

namespace MusicStagerWidget;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly MainViewModel _vm;
    private Forms.NotifyIcon? _notifyIcon;
    private readonly System.Drawing.Icon _appIcon;
    private bool _isExplicitExit = false;
    private bool _isFullyLoaded = false;
    private DispatcherTimer? _saveLocationDebounce;

    public bool IsDragOver { get; set; }

    public MainWindow()
    {
        App.Log("MainWindow ctor entered");
        InitializeComponent();
        App.Log("MainWindow InitializeComponent finished");

        _settings = AppSettings.Load();
        _vm = new MainViewModel(_settings);
        DataContext = _vm;

        // Custom taskbar and window icon
        _appIcon = TrayIconHelper.CreateMusicStagerIcon();
        Icon = TrayIconHelper.CreateMusicStagerImageSource(_appIcon);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += (s, e) => App.Log($"MainWindow Closed event: isExplicit={_isExplicitExit}");
        LocationChanged += MainWindow_LocationChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        InitTrayIcon();
        App.Log("MainWindow ctor finished");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        App.Log("MainWindow_Loaded entered");
        RestorePosition();
        _isFullyLoaded = true;
        App.Log("MainWindow_Loaded finished");
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RestorePosition();
        });
    }

    private void RestorePosition()
    {
        try
        {
            var allScreens = Forms.Screen.AllScreens;
            if (allScreens.Length == 0) return;

            var primaryScreen = Forms.Screen.PrimaryScreen ?? allScreens[0];

            double w = ActualWidth > 0 ? ActualWidth : 440;
            double h = ActualHeight > 0 ? ActualHeight : 350;

            if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue)
            {
                double savedLeft = _settings.WindowLeft.Value;
                double savedTop = _settings.WindowTop.Value;

                // 1. Check if coordinates lie inside any current screen
                var samplePt = new System.Drawing.Point((int)savedLeft + 20, (int)savedTop + 20);
                Forms.Screen? targetScreen = allScreens.FirstOrDefault(s => s.Bounds.Contains(samplePt));

                // 2. If not found by point, check if we saved a specific monitor
                if (targetScreen == null && !string.IsNullOrEmpty(_settings.SavedScreenDeviceName))
                {
                    targetScreen = allScreens.FirstOrDefault(s => s.DeviceName.Equals(_settings.SavedScreenDeviceName, StringComparison.OrdinalIgnoreCase));
                }

                if (targetScreen == null && _settings.IsPrimaryScreen == true)
                {
                    targetScreen = primaryScreen;
                }

                // If found valid screen, clamp inside working area
                if (targetScreen != null)
                {
                    var wa = targetScreen.WorkingArea;
                    Left = Math.Max(wa.Left, Math.Min(savedLeft, wa.Right - w));
                    Top = Math.Max(wa.Top, Math.Min(savedTop, wa.Bottom - h));
                    return;
                }
            }

            // Fallback: place on Primary monitor bottom-right
            var pwa = primaryScreen.WorkingArea;
            Left = pwa.Right - w - 20;
            Top = pwa.Bottom - h - 60;
        }
        catch { }
    }

    private void SaveCurrentPosition()
    {
        if (!_isFullyLoaded) return;
        if (WindowState != WindowState.Normal) return;

        try
        {
            var pt = new System.Drawing.Point((int)Left + 40, (int)Top + 40);
            var screen = Forms.Screen.FromPoint(pt);

            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            if (screen != null)
            {
                _settings.SavedScreenDeviceName = screen.DeviceName;
                _settings.IsPrimaryScreen = screen.Primary;
            }
            _settings.Save();
        }
        catch { }
    }

    private void MainWindow_LocationChanged(object? sender, EventArgs e)
    {
        if (!_isFullyLoaded) return;
        if (WindowState != WindowState.Normal) return;

        _saveLocationDebounce?.Stop();
        _saveLocationDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveLocationDebounce.Tick += (s, ev) =>
        {
            _saveLocationDebounce?.Stop();
            SaveCurrentPosition();
        };
        _saveLocationDebounce.Start();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        App.Log($"MainWindow_Closing entered: isExplicitExit={_isExplicitExit}");
        if (!_isExplicitExit)
        {
            // Minimize to tray on close
            e.Cancel = true;
            Hide();
            App.Log("MainWindow_Closing: canceled and hidden to tray");
            return;
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _appIcon?.Dispose();
        App.Log("MainWindow_Closing finished");
    }

    private void InitTrayIcon()
    {
        try
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _appIcon,
                Text = "(idwr) Music Stager 0.1 b6",
                Visible = true
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Показать виджет", null, (s, e) => ShowAndActivate());
            contextMenu.Items.Add("Открыть папку iTunes", null, (s, e) => _vm.OpenItunesFolder());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Выход", null, (s, e) =>
            {
                _isExplicitExit = true;
                Close();
                WpfApplication.Current.Shutdown();
            });

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowAndActivate();
        }
        catch { }
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.AlwaysOnTop = !_vm.AlwaysOnTop;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleSettings();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveSettings();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void TagScannerButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenInTagScanner();
    }

    private void OpenItunesButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenItunesFolder();
    }

    private void OpenSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenSourceFolder();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку с релизом"
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            _ = _vm.LoadPathsAsync(new[] { dialog.FolderName });
        }
    }

    private void BrowseItunesFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку Automatically Add to iTunes",
            InitialDirectory = _vm.SettingItunesFolder
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            _vm.SettingItunesFolder = dlg.FolderName;
        }
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите исполняемый файл ffmpeg.exe",
            Filter = "Исполняемые файлы (ffmpeg.exe)|ffmpeg.exe|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FileName))
        {
            _vm.SettingFfmpegPath = dlg.FileName;
        }
    }

    private void BrowseTagScanner_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите исполняемый файл Tagscan.exe",
            Filter = "Исполняемые файлы (Tagscan.exe)|Tagscan.exe|Все файлы (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FileName))
        {
            _vm.SettingTagScannerPath = dlg.FileName;
        }
    }

    private void OpenTelegram_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "https://t.me/indewer", UseShellExecute = true }); } catch { }
    }

    private void OpenEmail_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "mailto:indewer@ya.ru", UseShellExecute = true }); } catch { }
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = "https://github.com/indewer/idwr-Music-Stager", UseShellExecute = true }); } catch { }
    }

    private void CoverImage_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите изображение обложки (будет оптимизировано в 600x600 JPEG)",
            Filter = "Изображения (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true && File.Exists(dialog.FileName))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                _vm.SetCoverFromBytes(bytes);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"Ошибка загрузки картинки: {ex.Message}", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void AddSuffix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string suffix)
        {
            _vm.AppendSuffixToAlbumArtist(suffix);
        }
    }

    private void IncAlbumNum_Click(object sender, RoutedEventArgs e)
    {
        _vm.AlbumNumber++;
    }

    private void DecAlbumNum_Click(object sender, RoutedEventArgs e)
    {
        _vm.AlbumNumber--;
    }

    private void ToggleTrackReview_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleTrackReview();
    }

    private void SelectAllRename_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectAllTracksForRename();
    }

    private void DeselectAllRename_Click(object sender, RoutedEventArgs e)
    {
        _vm.DeselectAllTracksForRename();
    }

    private void ToggleTrackList_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleTrackList();
    }

    private void TitleCaseAll_Click(object sender, RoutedEventArgs e)
    {
        _vm.ApplyTitleCaseToAll();
    }

    private void AlbumTitleCase_Click(object sender, RoutedEventArgs e)
    {
        _vm.ApplyTitleCaseToAlbum();
    }

    private void GenreDropdownBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm.OpenGenrePopup();
    }

    private void GenreItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string genreName)
        {
            _vm.ToggleGenre(genreName);
        }
    }

    private void AddCustomGenre_Click(object sender, RoutedEventArgs e)
    {
        _vm.AddCustomGenre();
    }

    private async void DispatchToItunes_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ProcessAndDispatchAsync();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.Reset();
    }

    #region Drag and Drop Handling

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            IsDragOver = true;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        IsDragOver = false;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        IsDragOver = false;
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            string[]? paths = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (paths != null && paths.Length > 0)
            {
                _ = _vm.LoadPathsAsync(paths);
            }
        }
        e.Handled = true;
    }

    #endregion
}
