using System.Globalization;
using MessedUpSearchA.Services.Ml;

// Печатает эмбеддинг и топ-5 для каждого переданного файла — в том же формате,
// в каком их печатает Python-эталон, чтобы сравнение было построчным.
if (args.Length < 3)
{
    Console.Error.WriteLine("использование: MlCheck <модель.onnx> <индекс.bin> <аудио...>");
    return 1;
}

using var service = new BeatSimilarityService(args[0], args[1]);

foreach (var path in args.Skip(2))
{
    try
    {
        var samples = AudioDecoder.Decode(path);
        var matches = service.Analyze(path);

        Console.WriteLine($"FILE\t{Path.GetFileName(path)}");
        Console.WriteLine($"SAMPLES\t{samples.Length}");

        foreach (var match in matches)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "MATCH\t{0}\t{1:F6}\t{2}", match.Artist, match.Similarity, match.Percent));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR\t{Path.GetFileName(path)}\t{ex.Message}");
    }
}

return 0;
