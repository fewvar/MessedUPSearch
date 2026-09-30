using System;
using System.IO;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MessedUpSearchA.Services.Audio;

/// <summary>
/// Обёртка над SoundFlow: один файл за раз, как в любом плеере.
///
/// Движок и устройство вывода поднимаются при первом ▶ и живут до выхода —
/// открывать звуковую карту на каждый бит долго и слышно (щелчок).
///
/// Грабли: провайдеру обязательно передавать формат устройства. Без него
/// SoundFlow не ресемплит, и mp3 на 44.1 кГц играет на 48 кГц устройства —
/// на 9% быстрее и выше по тону. Для битмейкера это хуже, чем тишина.
/// </summary>
public sealed class AudioPlayerService : IDisposable
{
    private static readonly AudioFormat Format = AudioFormat.DvdHq;

    private MiniAudioEngine? _engine;
    private AudioPlaybackDevice? _device;

    private FileStream? _stream;
    private StreamDataProvider? _provider;
    private SoundPlayer? _player;

    private float _volume = 0.8f;

    /// <summary>Трек доиграл до конца сам. Приходит из аудиопотока — подписчик переносит в UI.</summary>
    public event Action? Ended;

    public bool IsOpen => _player is not null;

    public bool IsPlaying => _player?.State == PlaybackState.Playing;

    public double Position => _player?.Time ?? 0;

    public double Duration => _player?.Duration ?? 0;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_player is not null)
                _player.Volume = _volume;
        }
    }

    public void Open(string path)
    {
        Close();
        EnsureDevice();

        _stream = File.OpenRead(path);

        try
        {
            _provider = new StreamDataProvider(_engine!, Format, _stream);
            _player = new SoundPlayer(_engine!, Format, _provider) { Volume = _volume };
            _player.PlaybackEnded += OnPlaybackEnded;
            _device!.MasterMixer.AddComponent(_player);
        }
        catch
        {
            Close();
            throw;
        }
    }

    /// <summary>
    /// Короткий звук интерфейса поверх того, что играет (<see cref="UiSounds"/>): свой компонент в микшере,
    /// после окончания убирается. samples — стерео float 48 кГц, как у устройства.
    /// </summary>
    public void PlayEffect(float[] samples, float volume)
    {
        EnsureDevice();
        var provider = new RawDataProvider(samples, 48000);
        var effect = new SoundPlayer(_engine!, Format, provider) { Volume = volume };
        effect.PlaybackEnded += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _device?.MasterMixer.RemoveComponent(effect);
            effect.Dispose();
            provider.Dispose();
        });
        _device!.MasterMixer.AddComponent(effect);
        effect.Play();
    }

    public void Play() => _player?.Play();

    public void Pause() => _player?.Pause();

    public void Seek(double seconds)
    {
        if (_player is null)
            return;

        var target = Math.Clamp(seconds, 0, Math.Max(0, Duration - 0.05));
        _player.Seek(TimeSpan.FromSeconds(target), SeekOrigin.Begin);
    }

    public void Close()
    {
        if (_player is not null)
        {
            _player.PlaybackEnded -= OnPlaybackEnded;
            _player.Stop();
            _device?.MasterMixer.RemoveComponent(_player);
            _player.Dispose();
            _player = null;
        }

        _provider?.Dispose();
        _provider = null;

        _stream?.Dispose();
        _stream = null;
    }

    private void EnsureDevice()
    {
        if (_device is not null)
            return;

        _engine = new MiniAudioEngine();
        _device = _engine.InitializePlaybackDevice(null, Format);
        _device.Start();
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => Ended?.Invoke();

    public void Dispose()
    {
        Close();
        _device?.Stop();
        _device?.Dispose();
        _engine?.Dispose();
    }
}
