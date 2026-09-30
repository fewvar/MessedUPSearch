using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;
using MessedUpSearchA.Services;
using MessedUpSearchA.Services.Audio;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Parsing;

namespace MessedUpSearchA.ViewModels;

/// <summary>
/// Что можно поставить в плеер: бит с диска или трек артиста из сети.
/// Путь к звуку отдаётся лениво — трек артиста сначала надо скачать.
/// </summary>
public sealed class PlayItem
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public int? BeatId { get; init; }

    /// <summary>true — файл временный, после переключения его удаляем.</summary>
    public bool IsTemporary { get; init; }

    public Func<CancellationToken, Task<string>> GetPathAsync { get; init; } =
        _ => Task.FromResult(string.Empty);
}

/// <summary>
/// Один плеер на приложение: живёт в главном окне, поэтому звук не обрывается
/// при переходе между вкладками.
///
/// Позицию опрашиваем таймером в UI-потоке, а не событиями SoundFlow: те приходят
/// из аудиопотока, и трогать из них привязки нельзя.
/// </summary>
public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private const double SeekStepSeconds = 5;

    /// <summary>Дальше этого ⏮ перематывает в начало, а не на предыдущий трек — как у всех плееров.</summary>
    private const double RestartThresholdSeconds = 3;

    private readonly AudioPlayerService _audio = new();
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private readonly IReadOnlyDictionary<string, IAudioResolver> _resolvers = AudioResolvers.CreateAll();

    private List<PlayItem> _playlist = new();
    private int _index = -1;
    private string _temporaryFile = string.Empty;
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// Откуда брать актуальный список битов для ⏭: фильтры могли поменяться,
    /// пока бит играл, и следующим должен идти следующий из того, что видно сейчас.
    /// </summary>
    public Func<IReadOnlyList<Beat>>? BeatListProvider { get; set; }

    /// <summary>Какой бит играет — чтобы подсветить его строку. null — играет не бит.</summary>
    public event Action<int?>? PlayingBeatChanged;

    [ObservableProperty] private bool _hasTrack;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Caption))] private bool _isLoading;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Caption))] private string _subtitle = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Caption))] private string _status = string.Empty;
    [ObservableProperty] private string _timeLabel = "0:00 / 0:00";

    /// <summary>Длительность в секундах — волне для подписи «куда перемотаешь» при наведении.</summary>
    [ObservableProperty] private double _duration;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private float[]? _peaks;
    [ObservableProperty] private double _volume;

    /// <summary>Вторая строка под названием: загрузка, ошибка или BPM и тональность.</summary>
    public string Caption =>
        IsLoading ? Localizer.Instance["Player.Loading"]
        : !string.IsNullOrEmpty(Status) ? Status
        : Subtitle;

    public PlayerViewModel(AppSettings settings)
    {
        UiSounds.Output = _audio;
        _settings = settings;
        _volume = Math.Clamp(settings.PlayerVolume, 0, 1);
        _audio.Volume = (float)_volume;

        // Трек доиграл — следующий. Событие из аудиопотока, поэтому через диспетчер.
        _audio.Ended += () => Dispatcher.UIThread.Post(() => _ = NextAsync(auto: true));

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());

        CleanTemporaryDirectory();
    }

    private static string TemporaryDirectory => Path.Combine(AppPaths.DataDir, "tmp-audio", "player");

    partial void OnVolumeChanged(double value)
    {
        _audio.Volume = (float)value;
        _settings.PlayerVolume = Math.Round(value, 2);
        _settings.Save();
    }

    /// <summary>▶ в строке бита. Тот же бит — пауза/продолжить, другой — с начала.</summary>
    public void PlayBeat(Beat beat)
    {
        if (_index >= 0 && _index < _playlist.Count && _playlist[_index].BeatId == beat.Id && HasTrack)
        {
            TogglePlay();
            return;
        }

        var beats = BeatListProvider?.Invoke() ?? new[] { beat };
        _playlist = beats.Select(FromBeat).ToList();

        var index = _playlist.FindIndex(p => p.BeatId == beat.Id);
        if (index < 0)
        {
            _playlist.Insert(0, FromBeat(beat));
            index = 0;
        }

        _ = LoadAsync(index);
    }

    /// <summary>▶ у трека артиста: плейлист — все его треки, ⏭ идёт по ним.</summary>
    public void PlayArtistTracks(IReadOnlyList<ArtistTrack> tracks, int index, string artist)
    {
        _playlist = tracks.Select(t => FromArtistTrack(t, artist)).ToList();
        _ = LoadAsync(Math.Clamp(index, 0, _playlist.Count - 1));
    }

    [RelayCommand]
    public void TogglePlay()
    {
        if (!_audio.IsOpen)
            return;

        if (_audio.IsPlaying)
            _audio.Pause();
        else
            _audio.Play();

        Tick();
    }

    [RelayCommand]
    private Task Next() => NextAsync(auto: false);

    [RelayCommand]
    private Task Previous()
    {
        if (_audio.Position > RestartThresholdSeconds || _index <= 0)
        {
            _audio.Seek(0);
            Tick();
            return Task.CompletedTask;
        }

        return LoadAsync(_index - 1);
    }

    /// <summary>Клик по волне: доля от 0 до 1.</summary>
    [RelayCommand]
    private void SeekTo(double fraction)
    {
        _audio.Seek(fraction * _audio.Duration);
        Tick();
    }

    public void SeekBy(double seconds)
    {
        _audio.Seek(_audio.Position + seconds);
        Tick();
    }

    public void SeekForward() => SeekBy(SeekStepSeconds);

    public void SeekBackward() => SeekBy(-SeekStepSeconds);

    private async Task NextAsync(bool auto)
    {
        RefreshBeatPlaylist();

        if (_index + 1 < _playlist.Count)
        {
            await LoadAsync(_index + 1);
            return;
        }

        // Список кончился: сам по себе — останавливаемся в начале последнего трека.
        if (auto)
        {
            _audio.Pause();
            _audio.Seek(0);
            Tick();
        }
    }

    /// <summary>Играет бит — пересобираем плейлист по тому, что видно в таблице сейчас.</summary>
    private void RefreshBeatPlaylist()
    {
        if (_index < 0 || _index >= _playlist.Count || _playlist[_index].BeatId is not { } currentId)
            return;

        var beats = BeatListProvider?.Invoke();
        if (beats is null)
            return;

        var fresh = beats.Select(FromBeat).ToList();
        var position = fresh.FindIndex(p => p.BeatId == currentId);

        // Играющий бит отфильтровали — идём по старому списку, чтобы не прыгнуть в начало.
        if (position < 0)
            return;

        _playlist = fresh;
        _index = position;
    }

    private async Task LoadAsync(int index)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        var item = _playlist[index];
        _index = index;

        _audio.Close();
        DeleteTemporaryFile();

        HasTrack = true;
        Title = item.Title;
        Subtitle = item.Subtitle;
        Peaks = null;
        Progress = 0;
        Status = string.Empty;
        IsLoading = true;
        PlayingBeatChanged?.Invoke(item.BeatId);

        try
        {
            var path = await item.GetPathAsync(ct);

            if (ct.IsCancellationRequested)
            {
                // Трек артиста успел докачаться, но его уже переключили — файл никому не нужен.
                if (item.IsTemporary)
                    AudioFetcher.TryDelete(path);
                return;
            }

            if (item.IsTemporary)
                _temporaryFile = path;

            _audio.Open(path);
            _audio.Play();
            _timer.Start();
            IsLoading = false;
            Tick();

            Peaks = await WaveformService.GetPeaksAsync(path, ct);
        }
        catch (OperationCanceledException)
        {
            // Пользователь уже переключил трек — этот не нужен.
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
                return;

            IsLoading = false;
            IsPlaying = false;
            Status = ex is FileNotFoundException
                ? Localizer.Instance["Player.NoFile"]
                : Localizer.Instance.Format("Player.Failed", ex.Message);
            AppLog.Write($"плеер: {item.Title}: {ex.Message}");
        }
    }

    private void Tick()
    {
        var position = _audio.Position;
        var duration = _audio.Duration;

        IsPlaying = _audio.IsPlaying;
        Progress = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        TimeLabel = $"{Format(position)} / {Format(duration)}";
        Duration = duration;
    }

    private static string Format(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private static PlayItem FromBeat(Beat beat) => new()
    {
        Title = beat.BeatName,
        Subtitle = string.Join(" · ", new[] { beat.Bpm > 0 ? $"{beat.Bpm} BPM" : "", beat.Key }
            .Where(s => !string.IsNullOrWhiteSpace(s))),
        BeatId = beat.Id,
        GetPathAsync = _ => Task.FromResult(beat.FilePath)
    };

    private PlayItem FromArtistTrack(ArtistTrack track, string artist) => new()
    {
        Title = track.Title,
        Subtitle = $"{artist} · {track.SourcePlatform}",
        IsTemporary = true,
        GetPathAsync = async ct =>
        {
            if (!_resolvers.TryGetValue(track.SourcePlatform, out var resolver))
                throw new InvalidOperationException(Localizer.Instance["Player.NoAudio"]);

            var audio = await resolver.ResolveAsync(track.SourceTrackId, track.Url ?? string.Empty, ct)
                        ?? throw new InvalidOperationException(Localizer.Instance["Player.NoAudio"]);

            return await AudioFetcher.DownloadAsync(audio, TemporaryDirectory, ct);
        }
    };

    private void DeleteTemporaryFile()
    {
        if (string.IsNullOrEmpty(_temporaryFile))
            return;

        AudioFetcher.TryDelete(_temporaryFile);
        _temporaryFile = string.Empty;
    }

    private static void CleanTemporaryDirectory()
    {
        try
        {
            if (Directory.Exists(TemporaryDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(TemporaryDirectory, "*.mp3"))
                    AudioFetcher.TryDelete(file);
            }
        }
        catch
        {
            // Не вышло сейчас — выйдет при следующем запуске.
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _loadCts?.Cancel();
        _audio.Dispose();
        DeleteTemporaryFile();
    }
}
