using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WpfMessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;

namespace MusicStagerWidget;

public class TrackViewModel : INotifyPropertyChanged
{
    public TrackMetadata Model { get; }
    public uint Number => Model.TrackNumber;
    public string NumberText => $"{Number:D2}.";

    private string _originalTitle = "";
    public string OriginalTitle
    {
        get => _originalTitle;
        private set
        {
            if (_originalTitle != value)
            {
                _originalTitle = value;
                OnPropertyChanged();
            }
        }
    }

    private string _newTitle = "";
    public string NewTitle
    {
        get => _newTitle;
        set
        {
            if (_newTitle != value)
            {
                _newTitle = value;
                if (IsSelectedForRename)
                {
                    Model.Title = value;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveTitle));
                OnPropertyChanged(nameof(IsModified));
            }
        }
    }

    private bool _isSelectedForRename = true;
    public bool IsSelectedForRename
    {
        get => _isSelectedForRename;
        set
        {
            if (_isSelectedForRename != value)
            {
                _isSelectedForRename = value;
                Model.Title = value ? NewTitle : OriginalTitle;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveTitle));
            }
        }
    }

    public string EffectiveTitle => IsSelectedForRename ? NewTitle : OriginalTitle;

    public bool IsModified => !string.Equals(OriginalTitle, NewTitle, StringComparison.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

    public TrackViewModel(TrackMetadata model)
    {
        Model = model;
        _originalTitle = model.Title;
        _newTitle = model.Title;
        _isSelectedForRename = true;
    }
}

public class GenreItemViewModel : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string CountBadge => $"{Count}×";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private AlbumBatchInfo? _currentBatch;

    private bool _isLoaded;
    private bool _isProcessing;
    private double _progressValue;
    private string _progressText = "";
    private string _statusMessage = "";

    private string _artist = "";
    private string _albumArtist = "";
    private string _album = "";
    private string _year = "";
    private string _genre = "";
    private string _trackCountText = "";
    private string _formatSummary = "";

    private ImageSource? _coverSource;
    private byte[]? _coverBytes;

    private bool _isAlbum = true;
    private bool _isEP = false;
    private bool _isSingle = false;
    private int _albumNumber = 1;

    private bool _isDirectorCut = false;
    private bool _isDeclipped = false;

    private string _musicBrainzStatus = "";
    private bool _isSearchingMusicBrainz = false;
    private bool _isSettingsOpen = false;
    private string _settingItunesFolder = "";
    private string _settingFfmpegPath = "";
    private string _settingTagScannerPath = "";
    private int _settingMaxArtworkDimension = 600;
    private int _settingJpegQuality = 95;

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            if (SetField(ref _isSettingsOpen, value))
            {
                OnPropertyChanged(nameof(IsWidgetViewVisible));
                OnPropertyChanged(nameof(IsSettingsViewVisible));
            }
        }
    }

    public bool IsWidgetViewVisible => !_isSettingsOpen;
    public bool IsSettingsViewVisible => _isSettingsOpen;

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows != value)
            {
                _settings.StartWithWindows = value;
                AppSettings.SetStartup(value);
                _settings.Save();
                OnPropertyChanged();
            }
        }
    }

    public string SettingItunesFolder
    {
        get => _settingItunesFolder;
        set => SetField(ref _settingItunesFolder, value);
    }

    public string SettingFfmpegPath
    {
        get => _settingFfmpegPath;
        set => SetField(ref _settingFfmpegPath, value);
    }

    public string SettingTagScannerPath
    {
        get => _settingTagScannerPath;
        set => SetField(ref _settingTagScannerPath, value);
    }

    public int SettingMaxArtworkDimension
    {
        get => _settingMaxArtworkDimension;
        set => SetField(ref _settingMaxArtworkDimension, value);
    }

    public int SettingJpegQuality
    {
        get => _settingJpegQuality;
        set => SetField(ref _settingJpegQuality, value);
    }

    public void OpenSettings()
    {
        SettingItunesFolder = _settings.ItunesImportFolder;
        SettingFfmpegPath = _settings.FfmpegPath;
        SettingTagScannerPath = _settings.TagScannerPath;
        SettingMaxArtworkDimension = _settings.MaxArtworkDimension;
        SettingJpegQuality = _settings.JpegQuality;
        IsSettingsOpen = true;
    }

    public void SaveSettings()
    {
        _settings.ItunesImportFolder = SettingItunesFolder;
        _settings.FfmpegPath = SettingFfmpegPath;
        _settings.TagScannerPath = SettingTagScannerPath;
        _settings.MaxArtworkDimension = SettingMaxArtworkDimension;
        _settings.JpegQuality = SettingJpegQuality;
        _settings.Save();
        IsSettingsOpen = false;
    }

    public void ToggleSettings()
    {
        if (IsSettingsOpen)
        {
            SaveSettings();
        }
        else
        {
            OpenSettings();
        }
    }

    private CancellationTokenSource? _processCts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Settings => _settings;

    public bool AlwaysOnTop
    {
        get => _settings.AlwaysOnTop;
        set
        {
            if (_settings.AlwaysOnTop != value)
            {
                _settings.AlwaysOnTop = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool DeleteSource
    {
        get => _settings.DeleteSourceAfterImport;
        set
        {
            if (_settings.DeleteSourceAfterImport != value)
            {
                _settings.DeleteSourceAfterImport = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        set
        {
            if (SetField(ref _isLoaded, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsEmpty => !_isLoaded;

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (SetField(ref _isProcessing, value))
            {
                OnPropertyChanged(nameof(IsNotProcessing));
            }
        }
    }

    public bool IsNotProcessing => !_isProcessing;

    public double ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetField(ref _progressText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string Artist
    {
        get => _artist;
        set
        {
            if (SetField(ref _artist, value))
            {
                // Auto-suggest Album Artist from known mapping
                if (!string.IsNullOrWhiteSpace(_artist))
                {
                    AlbumArtist = _settings.GetAlbumArtist(_artist);
                }
            }
        }
    }

    public string AlbumArtist
    {
        get => _albumArtist;
        set => SetField(ref _albumArtist, value);
    }

    public string Album
    {
        get => _album;
        set => SetField(ref _album, value);
    }

    public string Year
    {
        get => _year;
        set => SetField(ref _year, value);
    }

    public string Genre
    {
        get => _genre;
        set
        {
            if (SetField(ref _genre, value))
            {
                UpdateGenreSelectionFlags();
            }
        }
    }

    public ObservableCollection<TrackViewModel> TrackList { get; } = new();

    private bool _isTrackListExpanded = false;
    public bool IsTrackListExpanded
    {
        get => _isTrackListExpanded;
        set => SetField(ref _isTrackListExpanded, value);
    }

    public void ToggleTrackList() => IsTrackListExpanded = !IsTrackListExpanded;

    private bool _isTrackReviewExpanded = false;
    public bool IsTrackReviewExpanded
    {
        get => _isTrackReviewExpanded;
        set
        {
            if (SetField(ref _isTrackReviewExpanded, value))
            {
                OnPropertyChanged(nameof(TrackReviewArrow));
            }
        }
    }

    public string TrackReviewArrow => IsTrackReviewExpanded ? "▲" : "▼";

    public string TrackReviewSummaryText
    {
        get
        {
            int selected = TrackList.Count(t => t.IsSelectedForRename);
            int total = TrackList.Count;
            return $"{selected} из {total} к замене";
        }
    }

    public void ToggleTrackReview() => IsTrackReviewExpanded = !IsTrackReviewExpanded;

    public void SelectAllTracksForRename()
    {
        foreach (var tr in TrackList)
        {
            tr.IsSelectedForRename = true;
        }
        OnPropertyChanged(nameof(TrackReviewSummaryText));
    }

    public void DeselectAllTracksForRename()
    {
        foreach (var tr in TrackList)
        {
            tr.IsSelectedForRename = false;
        }
        OnPropertyChanged(nameof(TrackReviewSummaryText));
    }

    public void ApplyTitleCaseToAll()
    {
        if (_currentBatch == null || _currentBatch.Tracks.Count == 0) return;

        // 1. Transform Album title
        if (!string.IsNullOrWhiteSpace(Album))
        {
            Album = TitleCaseHelper.ToTitleCaseWithWordBreak(Album);
        }

        // 2. Transform each track title
        foreach (var tr in TrackList)
        {
            string cased = TitleCaseHelper.ToTitleCaseWithWordBreak(tr.OriginalTitle);
            tr.NewTitle = cased;
            tr.IsSelectedForRename = true;
        }

        IsTrackReviewExpanded = true;
        StatusMessage = $"✔ Title Case сформирован для {TrackList.Count} треков. Проверьте список перед импортом.";
        OnPropertyChanged(nameof(TrackReviewSummaryText));
    }

    public void ApplyTitleCaseToAlbum()
    {
        if (!string.IsNullOrWhiteSpace(Album))
        {
            Album = TitleCaseHelper.ToTitleCaseWithWordBreak(Album);
        }
    }

    // --- Genre Popup and Selection Management ---
    public ObservableCollection<GenreItemViewModel> FilteredGenres { get; } = new();
    private List<GenreItemViewModel> _allGenres = new();

    private bool _isGenrePopupOpen = false;
    public bool IsGenrePopupOpen
    {
        get => _isGenrePopupOpen;
        set => SetField(ref _isGenrePopupOpen, value);
    }

    private string _genreSearchText = "";
    public string GenreSearchText
    {
        get => _genreSearchText;
        set
        {
            if (SetField(ref _genreSearchText, value))
            {
                OnPropertyChanged(nameof(HasGenreSearchText));
                OnPropertyChanged(nameof(CanAddCustomGenre));
                OnPropertyChanged(nameof(AddCustomGenrePrompt));
                FilterGenreList();
            }
        }
    }

    public bool HasGenreSearchText => !string.IsNullOrWhiteSpace(_genreSearchText);

    public bool CanAddCustomGenre
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_genreSearchText)) return false;
            string clean = _genreSearchText.Trim();
            return !_allGenres.Any(g => g.Name.Equals(clean, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string AddCustomGenrePrompt => $"+ Добавить «{_genreSearchText.Trim()}»";

    public void OpenGenrePopup()
    {
        RefreshGenreItems();
        IsGenrePopupOpen = true;
    }

    public void RefreshGenreItems()
    {
        var top = _settings.GetTopGenres();
        var selectedList = ParseCurrentGenres();

        _allGenres = top.Select(kv => new GenreItemViewModel
        {
            Name = kv.Key,
            Count = kv.Value,
            IsSelected = selectedList.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)
        }).ToList();

        FilterGenreList();
    }

    private void FilterGenreList()
    {
        FilteredGenres.Clear();
        string filter = _genreSearchText.Trim();
        var items = string.IsNullOrEmpty(filter)
            ? _allGenres
            : _allGenres.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredGenres.Add(item);
        }
    }

    public void ToggleGenre(string genreName)
    {
        if (string.IsNullOrWhiteSpace(genreName)) return;

        var selected = ParseCurrentGenres();
        int existingIdx = selected.FindIndex(g => g.Equals(genreName, StringComparison.OrdinalIgnoreCase));

        if (existingIdx >= 0)
        {
            selected.RemoveAt(existingIdx);
        }
        else
        {
            selected.Add(genreName.Trim());
        }

        Genre = string.Join(" | ", selected);
        UpdateGenreSelectionFlags();
    }

    public void AddCustomGenre()
    {
        string newName = _genreSearchText.Trim();
        if (string.IsNullOrEmpty(newName)) return;

        _settings.RegisterGenreUsage(newName);
        ToggleGenre(newName);
        GenreSearchText = "";
        RefreshGenreItems();
    }

    private List<string> ParseCurrentGenres()
    {
        if (string.IsNullOrWhiteSpace(Genre)) return new List<string>();
        return Genre.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(g => g.Trim())
                    .Where(g => !string.IsNullOrEmpty(g))
                    .ToList();
    }

    private void UpdateGenreSelectionFlags()
    {
        var selected = ParseCurrentGenres();
        foreach (var g in _allGenres)
        {
            g.IsSelected = selected.Contains(g.Name, StringComparer.OrdinalIgnoreCase);
        }
    }

    public string TrackCountText
    {
        get => _trackCountText;
        set => SetField(ref _trackCountText, value);
    }

    public string FormatSummary
    {
        get => _formatSummary;
        set => SetField(ref _formatSummary, value);
    }

    public ImageSource? CoverSource
    {
        get => _coverSource;
        set
        {
            if (SetField(ref _coverSource, value))
            {
                OnPropertyChanged(nameof(HasCover));
                OnPropertyChanged(nameof(HasNoCover));
            }
        }
    }

    public bool HasCover => _coverSource != null;
    public bool HasNoCover => _coverSource == null;

    public bool IsAlbum
    {
        get => _isAlbum;
        set
        {
            if (SetField(ref _isAlbum, value))
            {
                if (value) { _isEP = false; _isSingle = false; OnPropertyChanged(nameof(IsEP)); OnPropertyChanged(nameof(IsSingle)); }
                UpdateGeneratedComment();
            }
        }
    }

    public bool IsEP
    {
        get => _isEP;
        set
        {
            if (SetField(ref _isEP, value))
            {
                if (value) { _isAlbum = false; _isSingle = false; OnPropertyChanged(nameof(IsAlbum)); OnPropertyChanged(nameof(IsSingle)); }
                UpdateGeneratedComment();
            }
        }
    }

    public bool IsSingle
    {
        get => _isSingle;
        set
        {
            if (SetField(ref _isSingle, value))
            {
                if (value) { _isAlbum = false; _isEP = false; OnPropertyChanged(nameof(IsAlbum)); OnPropertyChanged(nameof(IsEP)); }
                UpdateGeneratedComment();
            }
        }
    }

    public int AlbumNumber
    {
        get => _albumNumber;
        set
        {
            int clamped = Math.Max(1, Math.Min(99, value));
            if (SetField(ref _albumNumber, clamped))
            {
                UpdateGeneratedComment();
            }
        }
    }

    public bool IsDirectorCut
    {
        get => _isDirectorCut;
        set
        {
            if (SetField(ref _isDirectorCut, value))
            {
                UpdateGeneratedComment();
            }
        }
    }

    public bool IsDeclipped
    {
        get => _isDeclipped;
        set
        {
            if (SetField(ref _isDeclipped, value))
            {
                UpdateGeneratedComment();
            }
        }
    }

    public string GeneratedComment
    {
        get
        {
            string prefix = "-A-01-";
            if (IsSingle) prefix = "-S-";
            else if (IsEP) prefix = "-EP-";
            else prefix = $"-A-{AlbumNumber:D2}-";

            // Modifiers: remove trailing dash to append
            string core = prefix.TrimEnd('-');

            if (IsDirectorCut)
                core += "-DC";

            if (IsDeclipped)
                core += "-dclp";

            return core + "-";
        }
    }

    public string MusicBrainzStatus
    {
        get => _musicBrainzStatus;
        set => SetField(ref _musicBrainzStatus, value);
    }

    public bool IsSearchingMusicBrainz
    {
        get => _isSearchingMusicBrainz;
        set => SetField(ref _isSearchingMusicBrainz, value);
    }

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
    }

    public void UpdateGeneratedComment()
    {
        OnPropertyChanged(nameof(GeneratedComment));
    }

    public void AppendSuffixToAlbumArtist(string suffix)
    {
        if (string.IsNullOrWhiteSpace(AlbumArtist))
        {
            AlbumArtist = (Artist + " " + suffix).Trim();
        }
        else
        {
            // If already contains suffix, don't duplicate
            if (!AlbumArtist.Contains(suffix, StringComparison.OrdinalIgnoreCase))
            {
                AlbumArtist = (AlbumArtist + " " + suffix).Trim();
            }
        }

        // Remember in known artists database
        if (!string.IsNullOrWhiteSpace(Artist))
        {
            _settings.RegisterArtist(Artist, AlbumArtist);
        }
    }

    public async Task LoadPathsAsync(string[] paths)
    {
        IsProcessing = true;
        StatusMessage = "Сканирование аудиофайлов и обложек...";

        try
        {
            var batch = await Task.Run(() => AudioProcessor.ScanInput(paths, _settings));
            if (batch.Tracks.Count == 0)
            {
                StatusMessage = "Аудиофайлы не найдены в указанной папке.";
                IsProcessing = false;
                return;
            }

            _currentBatch = batch;
            Artist = batch.DetectedArtist;
            AlbumArtist = batch.DetectedAlbumArtist;
            Album = batch.DetectedAlbum;
            Year = batch.DetectedYear > 0 ? batch.DetectedYear.ToString() : "";
            Genre = batch.DetectedGenre;
            FormatSummary = batch.FormatSummary;
            TrackCountText = $"{batch.Tracks.Count} трек(ов)";

            // Parse existing comments if any
            if (!string.IsNullOrWhiteSpace(batch.DetectedComment))
            {
                ParseExistingComment(batch.DetectedComment);
            }

            // Cover preview
            _coverBytes = batch.OptimizedCoverBytes;
            CoverSource = ImageOptimizer.CreatePreview(_coverBytes ?? Array.Empty<byte>());
            OnPropertyChanged(nameof(HasCover));

            IsLoaded = true;
            TrackList.Clear();
            foreach (var tr in batch.Tracks)
            {
                TrackList.Add(new TrackViewModel(tr));
            }
            IsTrackReviewExpanded = false;
            OnPropertyChanged(nameof(TrackReviewSummaryText));

            StatusMessage = $"Загружено {batch.Tracks.Count} треков. Готово к проверке.";

            // Start background MusicBrainz lookup for album number
            _ = LookupMusicBrainzAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка при сканировании: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void ParseExistingComment(string comment)
    {
        if (comment.Contains("-S-", StringComparison.OrdinalIgnoreCase))
        {
            IsSingle = true;
        }
        else if (comment.Contains("-EP-", StringComparison.OrdinalIgnoreCase))
        {
            IsEP = true;
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(comment, @"-A-(\d{2})-");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
            {
                IsAlbum = true;
                AlbumNumber = num;
            }
        }

        if (comment.Contains("-DC-", StringComparison.OrdinalIgnoreCase))
            IsDirectorCut = true;

        if (comment.Contains("-dclp-", StringComparison.OrdinalIgnoreCase) || comment.Contains("-DCm-", StringComparison.OrdinalIgnoreCase))
            IsDeclipped = true;
    }

    public async Task LookupMusicBrainzAsync()
    {
        if (string.IsNullOrWhiteSpace(Artist) || string.IsNullOrWhiteSpace(Album))
            return;

        IsSearchingMusicBrainz = true;
        MusicBrainzStatus = "Поиск в MusicBrainz...";

        try
        {
            var res = await MusicBrainzHelper.LookupDiscographyAsync(Artist, Album);
            if (res != null)
            {
                if (res.ReleaseType == "Single")
                {
                    IsSingle = true;
                    MusicBrainzStatus = "MusicBrainz: Сингл";
                }
                else if (res.ReleaseType == "EP")
                {
                    IsEP = true;
                    MusicBrainzStatus = "MusicBrainz: EP";
                }
                else if (res.AlbumNumber.HasValue)
                {
                    IsAlbum = true;
                    AlbumNumber = res.AlbumNumber.Value;
                    MusicBrainzStatus = $"MusicBrainz: Студийный альбом #{res.AlbumNumber.Value} из {res.TotalStudioAlbums}";
                }
            }
            else
            {
                MusicBrainzStatus = "Альбом не найден в MusicBrainz (выберите номер вручную)";
            }
        }
        catch
        {
            MusicBrainzStatus = "";
        }
        finally
        {
            IsSearchingMusicBrainz = false;
        }
    }

    public void SetCoverFromBytes(byte[] newBytes)
    {
        _coverBytes = ImageOptimizer.OptimizeImageBytes(newBytes, _settings.MaxArtworkDimension, _settings.JpegQuality);
        CoverSource = ImageOptimizer.CreatePreview(_coverBytes);
        OnPropertyChanged(nameof(HasCover));
    }

    public void OpenInTagScanner()
    {
        if (_currentBatch == null || string.IsNullOrWhiteSpace(_currentBatch.SourceDirectory))
        {
            WpfMessageBox.Show("Сначала перетащите папку с альбомом в виджет.", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string tagScannerExe = _settings.TagScannerPath;
        if (!File.Exists(tagScannerExe))
        {
            WpfMessageBox.Show($"TagScanner не найден по пути:\n{tagScannerExe}", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tagScannerExe,
                Arguments = $"\"{_currentBatch.SourceDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Не удалось запустить TagScanner: {ex.Message}", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void OpenItunesFolder()
    {
        try
        {
            if (Directory.Exists(_settings.ItunesImportFolder))
            {
                Process.Start("explorer.exe", $"\"{_settings.ItunesImportFolder}\"");
            }
            else
            {
                WpfMessageBox.Show($"Папка iTunes не найдена:\n{_settings.ItunesImportFolder}", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch { }
    }

    public void OpenSourceFolder()
    {
        try
        {
            if (_currentBatch != null && Directory.Exists(_currentBatch.SourceDirectory))
            {
                Process.Start("explorer.exe", $"\"{_currentBatch.SourceDirectory}\"");
            }
        }
        catch { }
    }

    public async Task ProcessAndDispatchAsync()
    {
        if (_currentBatch == null || _currentBatch.Tracks.Count == 0)
        {
            WpfMessageBox.Show("Нет треков для обработки.", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(AlbumArtist))
        {
            AlbumArtist = Artist;
        }

        // Save artist to dictionary
        if (!string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(AlbumArtist))
        {
            _settings.RegisterArtist(Artist, AlbumArtist);
        }

        // Save genre to dictionary and update frequency
        if (!string.IsNullOrWhiteSpace(Genre))
        {
            _settings.RegisterGenreUsage(Genre);
        }

        IsProcessing = true;
        ProgressValue = 0;
        _processCts = new CancellationTokenSource();

        uint yr = 0;
        uint.TryParse(Year, out yr);

        try
        {
            await AudioProcessor.ProcessBatchAsync(
                _currentBatch,
                Artist,
                AlbumArtist,
                Album,
                yr,
                Genre,
                GeneratedComment,
                _coverBytes,
                _settings,
                (cur, total, msg) =>
                {
                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        ProgressValue = (double)cur / total * 100.0;
                        ProgressText = $"{cur}/{total}";
                        StatusMessage = msg;
                    });
                },
                _processCts.Token
            );

            ProgressValue = 100;
            StatusMessage = $"✅ Успешно! {ProgressText} треков перемещены в iTunes.";
            WpfMessageBox.Show($"Готово! {ProgressText} треков отправлены в iTunes.\n\nТеги и обложка 600x600 вшиты.\niTunes автоматически разложит файлы по библиотеке.", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Information);

            Reset();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Обработка отменена пользователем.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
            WpfMessageBox.Show($"Ошибка при обработке:\n{ex.Message}", "Music Stager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void CancelProcessing()
    {
        _processCts?.Cancel();
    }

    public void Reset()
    {
        _currentBatch = null;
        IsLoaded = false;
        ProgressValue = 0;
        ProgressText = "";
        StatusMessage = "";
        Artist = "";
        AlbumArtist = "";
        Album = "";
        Year = "";
        Genre = "";
        TrackCountText = "";
        FormatSummary = "";
        CoverSource = null;
        _coverBytes = null;
        MusicBrainzStatus = "";
        IsDirectorCut = false;
        IsDeclipped = false;
        IsAlbum = true;
        AlbumNumber = 1;
        OnPropertyChanged(nameof(HasCover));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
