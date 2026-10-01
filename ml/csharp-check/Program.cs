using MessedUpSearchA.Services.Ml;

// Сверка EffNet + голова: C# против Python (ml/scripts/check_effnet.py).
//
//   MlCheck vectors <список.txt> <out.bin>
//
// Список — по пути на строку. Выход: int файлов, int размерность, затем на файл
// сырой вектор EffNet и финальный (после головы), float32. Не посчитался — нули.

// Письмо «Сообщить о проблеме»: MlCheck report — mailto, его длина и расшифрованный текст.
if (args.Length == 1 && args[0] == "report")
{
    MessedUpSearchA.Services.AppLog.Enabled = true;
    var uri = MessedUpSearchA.Services.SupportReport.BuildMailto("v1.1", "Что случилось и что ты делал перед этим?");
    Console.WriteLine($"длина {uri.Length}\n{Uri.UnescapeDataString(uri)}");
    return 0;
}

// Рассылка целиком на тестовом SMTP (127.0.0.1:2525, ml-скрипт не нужен — сервер в scratchpad):
//   MlCheck mail <папка данных-копия>
// Пароль кладётся в связку ключей и в конце удаляется. Печатает журнал писем, отметки в CRM и лимит.
if (args.Length == 2 && args[0] == "mail")
{
    Environment.SetEnvironmentVariable(MessedUpSearchA.Data.AppPaths.DataDirVariable, args[1]);
    MessedUpSearchA.Services.AppLog.Enabled = true;
    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.Migrate(db.Database);
        var artists = db.Artists.OrderBy(a => a.Id).Take(3).ToList();
        artists[0].Email = "one@ok.test";
        artists[1].Email = "bad@reject.test";
        artists[2].Email = "two@ok.test";
        db.Beats.First().ShareUrl = "https://example.com/beat";
        db.SaveChanges();
    }

    var settings = new MessedUpSearchA.Data.AppSettings
    {
        MailAddress = "test@local.test", MailSenderName = "fewvar", SmtpHost = "127.0.0.1", SmtpPort = 2525,
        MailDailyLimit = 50
    };
    var key = MessedUpSearchA.Services.Mail.MailQueue.PasswordKey(settings.MailAddress);
    Console.WriteLine($"связка ключей: запись {MessedUpSearchA.Services.SecretStore.Set(key, "secret")}, " +
                      $"чтение {(MessedUpSearchA.Services.SecretStore.Get(key) == "secret" ? "совпало" : "НЕ совпало")}");

    async Task RunQueue(MessedUpSearchA.Data.AppSettings s, int count)
    {
        var queue = new MessedUpSearchA.Services.Mail.MailQueue(() => s) { MinPauseSeconds = 1, MaxPauseSeconds = 2 };
        using var db = new MessedUpSearchA.Data.AppDbContext();
        var beat = db.Beats.First();
        var vm = new MessedUpSearchA.ViewModels.MailViewModel(queue, () => s);
        vm.Open(beat.Id, db.Artists.OrderBy(a => a.Id).Take(count).Select(a => a.Id).ToList());
        foreach (var r in vm.Recipients)
            Console.WriteLine($"  получатель {r.Nickname} <{r.Email}> выбран={r.IsSelected} {r.Warning}");
        Console.WriteLine($"  предпросмотр: {vm.PreviewSubject} | {vm.PreviewBody.Replace('\n', ' ')}");
        Console.WriteLine($"  можно отправить: {vm.CanSend} {vm.Problem} · {vm.Summary}");
        vm.SendCommand.Execute(null);
        await Task.Delay(300);
        while (queue.IsRunning) await Task.Delay(200);
    }

    MessedUpSearchA.Services.Toasts.Shown += t => Console.WriteLine($"  тост: {t}");
    Console.WriteLine("прогон 1: три артиста, один адрес отклоняется");
    await RunQueue(settings, 3);

    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        foreach (var m in db.OutgoingMails.ToList())
            Console.WriteLine($"  журнал: {m.ToAddress} {m.Status} id={(m.MessageId.Length > 0 ? "есть" : "нет")} {m.Error}");
        foreach (var a in db.Artists.OrderBy(a => a.Id).Take(3).ToList())
        {
            var link = db.SentBeatsLog.FirstOrDefault(l => l.ArtistId == a.Id);
            Console.WriteLine($"  CRM: {a.Nickname} статус «{a.CrmStatus}» отправлен={link?.IsSent}");
        }
    }

    Console.WriteLine("прогон 2: тот же бит тем же — защита от спама");
    await RunQueue(settings, 3);

    Console.WriteLine("прогон 3: неверный пароль");
    MessedUpSearchA.Services.SecretStore.Set(key, "wrong");
    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        db.OutgoingMails.RemoveRange(db.OutgoingMails);
        db.SaveChanges();
    }
    await RunQueue(settings, 1);

    Console.WriteLine("прогон 4: лимит 1 письмо в сутки");
    MessedUpSearchA.Services.SecretStore.Set(key, "secret");
    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        db.OutgoingMails.RemoveRange(db.OutgoingMails);
        db.SaveChanges();
    }
    settings.MailDailyLimit = 1;
    var limited = settings;
    int[] ids;
    using (var db = new MessedUpSearchA.Data.AppDbContext())
        ids = db.Artists.OrderBy(a => a.Id).Take(3).Select(a => a.Id).ToArray();
    {
        var queue = new MessedUpSearchA.Services.Mail.MailQueue(() => limited) { MinPauseSeconds = 1, MaxPauseSeconds = 1 };
        queue.Enqueue([
            new MessedUpSearchA.Services.Mail.MailJob(ids[0], 1, null, "pitch", "one@ok.test", "s1", "b1"),
            new MessedUpSearchA.Services.Mail.MailJob(ids[2], 999, null, "pitch", "two@ok.test", "s2", "b2")]);
        await Task.Delay(300);
        while (queue.IsRunning) await Task.Delay(200);
    }

    Console.WriteLine("лог:\n" + string.Join("\n", File.ReadLines(MessedUpSearchA.Services.AppLog.FilePath).Where(l => l.Contains("рассылк") || l.Contains("провер"))));
    MessedUpSearchA.Services.SecretStore.Delete(key);
    Console.WriteLine($"связка ключей после удаления: {MessedUpSearchA.Services.SecretStore.Get(key) ?? "пусто"}");
    return 0;
}

// Подстановка в шаблон без BPM / тональности: MlCheck render
if (args.Length == 1 && args[0] == "render")
{
    var a = new MessedUpSearchA.Models.Artist { Nickname = "nick" };
    foreach (var (bpm, key) in new[] { (150, "C#min"), (0, "C#min"), (150, ""), (0, "") })
        Console.WriteLine(MessedUpSearchA.Services.Mail.MailTemplates.Render(
            "бит: {beat} ({bpm} BPM, {key}). Ссылка {link}", a,
            new MessedUpSearchA.Models.Beat { BeatName = "pink", Bpm = bpm, Key = key, ShareUrl = "u" }, "me"));
    return 0;
}

// Подсказки контактов: MlCheck hints <ссылка на профиль> — описание с площадки и что из него достаётся.
if (args.Length == 2 && args[0] == "hints")
{
    string[] samples =
    [
        "booking: Name.Surname@Gmail.com | tg: @some_manager",
        "biz → beats (at) proton (dot) me  ig: @artist.nick",
        "t.me/artistchannel instagram.com/artist_ig/ instagram.com/p/xyz",
        "мейл для битов: beatz@mail.ru, инст @nick_ru",
        "Instagram: https://instagram.com/real_one telegram: https://t.me/real_tg big trap figure"
    ];
    foreach (var sample in samples)
        Console.WriteLine($"{sample}\n  -> {string.Join(", ", MessedUpSearchA.Services.Parsing.ContactHints.Extract(sample))}");

    var bio = await MessedUpSearchA.Services.Parsing.ContactHints.FetchBioAsync(args[1]);
    Console.WriteLine($"\nпрофиль: {bio?.Replace('\n', ' ')}\n  -> {string.Join(", ", MessedUpSearchA.Services.Parsing.ContactHints.Extract(bio))}");
    return 0;
}

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
