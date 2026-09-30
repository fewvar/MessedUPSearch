using MessedUpSearchA.Services.Ml;

// Сверка EffNet + голова: C# против Python (ml/scripts/check_effnet.py).
//
//   MlCheck vectors <список.txt> <out.bin>
//
// Список — по пути на строку. Выход: int файлов, int размерность, затем на файл
// сырой вектор EffNet и финальный (после головы), float32. Не посчитался — нули.

// Звуки интерфейса в WAV: MlCheck sounds <папка> — послушать и проверить уровни.
if (args.Length == 2 && args[0] == "sounds")
{
    Directory.CreateDirectory(args[1]);
    foreach (var sound in Enum.GetValues<MessedUpSearchA.Services.Audio.UiSound>())
    {
        var samples = MessedUpSearchA.Services.Audio.UiSounds.Samples(sound);
        using var wav = new BinaryWriter(File.Create(Path.Combine(args[1], $"{sound}.wav")));
        var bytes = samples.Length * 4;
        wav.Write("RIFF"u8); wav.Write(36 + bytes); wav.Write("WAVE"u8);
        wav.Write("fmt "u8); wav.Write(16); wav.Write((short)3); wav.Write((short)2); wav.Write(48000); wav.Write(48000 * 8); wav.Write((short)8); wav.Write((short)32);
        wav.Write("data"u8); wav.Write(bytes);
        foreach (var v in samples) wav.Write(v);
        Console.WriteLine($"{sound}: {samples.Length / 2 / 48000.0:F2} с, пик {samples.Max(Math.Abs):F2}");
    }
    return 0;
}

// Отладка входа: MlCheck decode <файл> <out.f32> — сэмплы 16 кГц, как их видит EffNet.
if (args.Length == 3 && args[0] == "decode")
{
    var samples = AudioDecoder.Decode(args[1], EffNetEmbedder.SampleRate);
    var bytes = new byte[samples.Length * sizeof(float)];
    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
    File.WriteAllBytes(args[2], bytes);
    return 0;
}

// Скорость анализа как в приложении: MlCheck time <файл> — EmbedFile (центральная минута, перемотка mp3).
if (args.Length == 2 && args[0] == "time")
{
    using var timed = new EffNetEmbedder(MlAssets.ModelPath, MlAssets.HeadPath);
    timed.EmbedFile(args[1]);                       // прогрев: первая сессия ONNX медленнее
    var sw = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 3; i++)
        timed.EmbedFile(args[1]);
    Console.WriteLine($"{Path.GetFileName(args[1])}: {sw.Elapsed.TotalSeconds / 3:F2} с на анализ");
    return 0;
}

if (args.Length != 3 || args[0] != "vectors")
{
    Console.Error.WriteLine("MlCheck vectors <список.txt> <out.bin>");
    return 1;
}

var paths = File.ReadAllLines(args[1]).Where(l => l.Length > 0).ToArray();
using var embedder = new EffNetEmbedder(MlAssets.ModelPath, MlAssets.HeadPath);
var dim = embedder.Dimension;

using var writer = new BinaryWriter(File.Create(args[2]));
writer.Write(paths.Length);
writer.Write(dim);

var clock = System.Diagnostics.Stopwatch.StartNew();
var failed = 0;
foreach (var path in paths)
{
    float[] raw, final;
    try
    {
        raw = embedder.EmbedRaw(AudioDecoder.Decode(path, EffNetEmbedder.SampleRate));
        final = embedder.Project(raw);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{Path.GetFileName(path)}: {ex.Message}");
        raw = new float[dim];
        final = new float[dim];
        failed++;
    }

    foreach (var v in raw) writer.Write(v);
    foreach (var v in final) writer.Write(v);
}

Console.WriteLine($"файлов {paths.Length}, не посчиталось {failed}, {clock.Elapsed.TotalSeconds / Math.Max(1, paths.Length):F2} с на файл");
return 0;
