using System;

namespace MessedUpSearchA.Services.Audio;

public enum UiSound
{
    /// <summary>Анализ бита готов — мягкий двухнотный «динь».</summary>
    AnalysisDone,

    /// <summary>Бит отмечен отправленным — короткий тик.</summary>
    Sent,

    /// <summary>★ поставлена — лёгкий щипок вверх.</summary>
    Favorite,

    /// <summary>Парсер закончил поиск — три ноты вверх.</summary>
    ParserDone
}

/// <summary>
/// Звуки интерфейса: всего четыре события, тихо, чтобы не надоедали. Синтезируются в коде при первом
/// использовании (синусы с мягкой огибающей) — без аудиофайлов и чужих лицензий. Играют через тот же
/// движок, что плеер (<see cref="AudioPlayerService.PlayEffect"/>), поверх бита, не останавливая его.
/// Выключаются галочкой «Звуки» в настройках.
/// </summary>
public static class UiSounds
{
    private const int Rate = 48000;       // формат устройства: 48 кГц, стерео, float
    private const float Volume = 0.35f;

    private static readonly float[]?[] Cache = new float[]?[Enum.GetValues<UiSound>().Length];

    public static bool Enabled { get; set; } = true;

    /// <summary>Плеер приложения — его устройство вывода и играет эффекты.</summary>
    public static AudioPlayerService? Output { get; set; }

    public static void Play(UiSound sound)
    {
        if (!Enabled || Output is null)
            return;

        try
        {
            var samples = Cache[(int)sound] ??= Render(sound);
            Output.PlayEffect(samples, Volume);
        }
        catch (Exception ex)
        {
            // Звук — украшение: нет устройства или оно занято — молча без звука.
            AppLog.Write($"звук интерфейса: {ex.Message}");
        }
    }

    /// <summary>Сэмплы звука (стерео float 48 кГц) — для проверки и выгрузки в WAV (MlCheck sounds).</summary>
    public static float[] Samples(UiSound sound) => Cache[(int)sound] ??= Render(sound);

    private static float[] Render(UiSound sound) => sound switch
    {
        UiSound.AnalysisDone => Mix(0.62, (659.3, 0.00, 0.22, 0.18), (987.8, 0.09, 0.20, 0.22)),
        UiSound.Sent => Mix(0.12, (1760.0, 0.00, 0.14, 0.035)),
        UiSound.Favorite => Mix(0.16, (1318.5, 0.00, 0.12, 0.06), (1975.5, 0.03, 0.08, 0.05)),
        UiSound.ParserDone => Mix(0.55, (523.3, 0.00, 0.16, 0.16), (659.3, 0.07, 0.16, 0.16), (784.0, 0.14, 0.18, 0.2)),
        _ => []
    };

    /// <summary>
    /// Ноты (частота, старт с, громкость, затухание с): синус + немного второй гармоники, атака 5 мс,
    /// экспоненциальный спад. На выходе — стерео с чередованием L R.
    /// </summary>
    private static float[] Mix(double seconds, params (double Freq, double Start, double Gain, double Decay)[] notes)
    {
        var frames = (int)(seconds * Rate);
        var mono = new double[frames];

        foreach (var (freq, start, gain, decay) in notes)
        {
            var from = (int)(start * Rate);
            for (var i = from; i < frames; i++)
            {
                var t = (i - from) / (double)Rate;
                var attack = Math.Min(1, t / 0.005);
                var envelope = attack * Math.Exp(-t / decay);
                var phase = 2 * Math.PI * freq * t;
                mono[i] += gain * envelope * (Math.Sin(phase) + 0.18 * Math.Sin(2 * phase));
            }
        }

        // Последние 10 мс — в ноль, чтобы не щёлкнуло на обрыве.
        var tail = Math.Min(frames, Rate / 100);
        for (var i = 0; i < tail; i++)
            mono[frames - 1 - i] *= i / (double)tail;

        var stereo = new float[frames * 2];
        for (var i = 0; i < frames; i++)
            stereo[2 * i] = stereo[2 * i + 1] = (float)Math.Clamp(mono[i], -1, 1);
        return stereo;
    }
}
