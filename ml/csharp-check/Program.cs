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
        var vm = new MessedUpSearchA.ViewModels.MailViewModel(queue, () => s, () => null);
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

// Сессии в DAW и фоновый режим: MlCheck daw <папка данных> — активное приложение, сопоставление,
// склейка сессий, автозапуск (HOME подменяется на папку данных, настоящий ~/Library не трогается).
if (args.Length == 2 && args[0] == "daw")
{
    Environment.SetEnvironmentVariable(MessedUpSearchA.Data.AppPaths.DataDirVariable, args[1]);
    var fg = MessedUpSearchA.Services.Foreground.Current();
    Console.WriteLine($"сейчас впереди: «{fg?.Name}» {fg?.BundleId} -> DAW: {MessedUpSearchA.Services.DawCatalog.Match(fg) ?? "нет"}");

    foreach (var (name, id) in new[] { ("GarageBand", "com.apple.garageband10"), ("FL Studio 2024", "com.image-line.flstudio"),
                 ("Ableton Live 12 Suite", "com.ableton.live"), ("Logic Pro", "com.apple.logic10"), ("REAPER", "com.cockos.reaper"),
                 ("Cubase 14", "com.steinberg.cubase14"), ("Studio Pro", "com.fender.studiopro"), ("Reason", "com.reasonstudios.reason"),
                 ("Safari", "com.apple.Safari"), ("Reasonable Notes", "com.x.notes"), ("Live Photos", "com.x.live") })
        Console.WriteLine($"  {name,-22} -> {MessedUpSearchA.Services.DawCatalog.Match(new(name, id, "")) ?? "—"}");

    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.Migrate(db.Database);
        db.DawSessions.RemoveRange(db.DawSessions);
        db.SaveChanges();
    }
    var tracker = new MessedUpSearchA.Services.DawTracker(() => new MessedUpSearchA.Data.AppSettings { DawTracking = true });
    var t0 = DateTime.Today.AddHours(14);
    // 14:00–14:30 FL, перерыв 8 мин (браузер), 14:38–14:50 FL, перерыв 30 мин, 15:20–15:25 FL, 15:26 Ableton
    for (var m = 0; m <= 30; m++) tracker.Record(t0.AddMinutes(m), "FL Studio");
    for (var m = 31; m < 38; m++) tracker.Record(t0.AddMinutes(m), null);
    for (var m = 38; m <= 50; m++) tracker.Record(t0.AddMinutes(m), "FL Studio");
    for (var m = 80; m <= 85; m++) tracker.Record(t0.AddMinutes(m), "FL Studio");
    tracker.Record(t0.AddMinutes(86), "Ableton Live");
    using (var db = new MessedUpSearchA.Data.AppDbContext())
        foreach (var s in db.DawSessions.OrderBy(s => s.Id))
            Console.WriteLine($"  сессия {s.App} {s.StartedAt[11..]}–{s.EndedAt[11..]}");
    Console.WriteLine("  (ждём: FL 14:00–14:50, FL 15:20–15:25, Ableton 15:26–15:26)");

    Environment.SetEnvironmentVariable("HOME", args[1]);
    Console.WriteLine($"автозапуск: «{MessedUpSearchA.Services.Autostart.Enable()}» (пусто = ок), включён={MessedUpSearchA.Services.Autostart.IsEnabled()}");
    var plist = Path.Combine(args[1], "Library", "LaunchAgents", "com.fewvar.messedupsearch.plist");
    Console.WriteLine(File.ReadAllText(plist));
    MessedUpSearchA.Services.Autostart.Disable();
    Console.WriteLine($"после выключения: включён={MessedUpSearchA.Services.Autostart.IsEnabled()}, файл есть={File.Exists(plist)}");
    return 0;
}

// Статистика на копии базы: MlCheck stats <папка данных> <дней> — сверяется с ml-скриптом в scratchpad.
if (args.Length == 3 && args[0] == "stats")
{
    Environment.SetEnvironmentVariable(MessedUpSearchA.Data.AppPaths.DataDirVariable, args[1]);
    MessedUpSearchA.Services.Localization.Localizer.Instance.Language = MessedUpSearchA.Services.Localization.AppLanguage.Russian;
    var days = int.Parse(args[2]);
    using var db = new MessedUpSearchA.Data.AppDbContext();
    var r = MessedUpSearchA.Services.Stats.StatsService.Build(db, DateTime.Today.AddDays(-days + 1),
        MessedUpSearchA.Services.Localization.Localizer.Instance.Culture);
    Console.WriteLine($"питчей {r.Pitches} отвечено {r.Answered} {r.ReplyRate:P0}");
    foreach (var row in r.ByTemplate) Console.WriteLine($" шаблон {row.Label} {row.Hits} / {row.Total}");
    Console.WriteLine(" дни " + string.Join(" ", r.ByWeekday.Select(x => $"{x.Label}:{x.Hits}/{x.Total}")));
    Console.WriteLine(" часы " + string.Join(" ", r.ByHours.Select(x => $"{x.Label[..2]}:{x.Hits}/{x.Total}")));
    Console.WriteLine($" время ответа [{string.Join(", ", r.ReplyTime.Select(x => x.Hits))}]");
    Console.WriteLine($" медиана {r.MedianReply}");
    Console.WriteLine($" фоллоу-апов {r.FollowUps} продано {r.Sold} фри {r.Free} новых битов {r.BeatsAdded}");
    Console.WriteLine($" биты: {string.Join("; ", r.TopBeats.Select(x => $"{x.Label} {x.ValueText}"))}");
    Console.WriteLine($" по неделям: {string.Join(" ", r.WeeklySent)}");
    return 0;
}

// Живая CRM на GreenMail (SMTP 3025 / IMAP 3143, пользователь test@local.test:secret):
//   MlCheck replies <папка данных-копия>
if (args.Length == 2 && args[0] == "replies")
{
    Environment.SetEnvironmentVariable(MessedUpSearchA.Data.AppPaths.DataDirVariable, args[1]);
    MessedUpSearchA.Services.AppLog.Enabled = true;
    MessedUpSearchA.Services.Toasts.Shown += t => Console.WriteLine($"  тост: {t}");
    int[] ids; int beatA, beatB;
    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.Migrate(db.Database);
        var three = db.Artists.OrderBy(a => a.Id).Take(3).ToList();
        for (var i = 0; i < 3; i++) { three[i].Email = $"a{i + 1}@ok.test"; three[i].CrmStatus = ""; }
        foreach (var b in db.Beats) b.ShareUrl = "https://example.com/" + b.Id;
        db.SaveChanges();
        ids = three.Select(a => a.Id).ToArray();
        beatA = db.Beats.OrderBy(b => b.Id).First().Id;
        beatB = db.Beats.OrderBy(b => b.Id).Skip(1).First().Id;
    }

    var settings = new MessedUpSearchA.Data.AppSettings
    {
        MailAddress = "test@local.test", MailSenderName = "fewvar", SmtpHost = "127.0.0.1", SmtpPort = 3025,
        ImapHost = "127.0.0.1", ImapPort = 3143, FollowUpDays = 0
    };
    var key = MessedUpSearchA.Services.Mail.MailQueue.PasswordKey(settings.MailAddress);
    MessedUpSearchA.Services.SecretStore.Set(key, "secret");

    async Task Drain(MessedUpSearchA.Services.Mail.MailQueue q) { await Task.Delay(300); while (q.IsRunning) await Task.Delay(200); }
    var queue = new MessedUpSearchA.Services.Mail.MailQueue(() => settings) { MinPauseSeconds = 1, MaxPauseSeconds = 1 };

    Console.WriteLine("1. питч трём артистам");
    var vm = new MessedUpSearchA.ViewModels.MailViewModel(queue, () => settings, () => null);
    vm.Open(beatA, ids);
    vm.SendCommand.Execute(null);
    await Drain(queue);

    Console.WriteLine("2. ответы: a1 по In-Reply-To с цитатой, a2 без заголовков HTML-письмом, посторонний спам");
    string pitchToA1;
    using (var db = new MessedUpSearchA.Data.AppDbContext())
        pitchToA1 = db.OutgoingMails.First(m => m.ToAddress == "a1@ok.test").MessageId;
    var account = new MessedUpSearchA.Services.Mail.MailAccount("test@local.test", "x", "secret", "127.0.0.1", 3025);
    async Task Inject(string from, string subject, MimeKit.MimeEntity body, string? inReplyTo)
    {
        var m = new MimeKit.MimeMessage();
        m.From.Add(MimeKit.MailboxAddress.Parse(from));
        m.To.Add(MimeKit.MailboxAddress.Parse("test@local.test"));
        m.Subject = subject; m.Body = body;
        if (inReplyTo is not null) m.InReplyTo = inReplyTo;
        using var smtp = new MailKit.Net.Smtp.SmtpClient();
        await smtp.ConnectAsync("127.0.0.1", 3025, MailKit.Security.SecureSocketOptions.None);
        await smtp.AuthenticateAsync("test@local.test", "secret");
        await smtp.SendAsync(m);
        await smtp.DisconnectAsync(true);
    }
    await Inject("a1@ok.test", "Re: бит", new MimeKit.TextPart("plain") { Text = "\n\nЙо, кидай ещё такие!\n\n> Привет, a1!\n> Послушал твои треки" }, pitchToA1);
    await Inject("a2@ok.test", "beat", new MimeKit.TextPart("html") { Text = "<div>yo bro, <b>how much</b> for exclusive?</div><div>thx</div>" }, null);
    await Inject("random@spam.test", "SEO services", new MimeKit.TextPart("plain") { Text = "buy now" }, null);

    var found = await MessedUpSearchA.Services.Mail.ReplyChecker.CheckAsync(settings);
    foreach (var f in found) Console.WriteLine($"  найден ответ: {f.Nickname}: «{f.Snippet}»");
    Console.WriteLine($"  повторная проверка нашла: {(await MessedUpSearchA.Services.Mail.ReplyChecker.CheckAsync(settings)).Count} (ждём 0), lastUid={settings.ImapLastUid}");

    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        foreach (var id in ids)
        {
            var a = db.Artists.First(x => x.Id == id);
            Console.WriteLine($"  CRM {a.Nickname} <{a.Email}>: «{a.CrmStatus}»");
        }
        var due = MessedUpSearchA.Services.Mail.CrmRules.DueFollowUps(db, 0);
        Console.WriteLine($"3. фоллоу-ап пора: {string.Join(", ", due.Select(d => db.Artists.First(a => a.Id == d.ArtistId).Email))} (ждём a3)");

        vm.OpenFollowUps(due);
        foreach (var r in vm.Recipients)
            Console.WriteLine($"  получатель {r.Email}: {r.BeatLine}, In-Reply-To={(r.InReplyTo is null ? "нет" : "есть")}, тема «{vm.PreviewSubject}»");
        Console.WriteLine($"  можно отправить: {vm.CanSend} {vm.Problem}");
    }
    vm.SendCommand.Execute(null);
    await Drain(queue);

    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        var fu = db.OutgoingMails.Where(m => m.Kind == "followup").ToList();
        Console.WriteLine($"  фоллоу-апов в журнале: {fu.Count}, после отправки пора: {MessedUpSearchA.Services.Mail.CrmRules.DueFollowUps(db, 0).Count} (ждём 0)");
    }

    Console.WriteLine("4. у a3 третье письмо без ответа (старое, 10 дней назад) — «молчун»");
    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        db.OutgoingMails.Add(new MessedUpSearchA.Models.OutgoingMail
        {
            ArtistId = ids[2], BeatId = beatB, Kind = "pitch", ToAddress = "a3@ok.test", Subject = "s", Body = "b",
            MessageId = "<old@local.test>", Status = "sent", SentAt = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd HH:mm")
        });
        db.SaveChanges();
    }
    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        var silent = MessedUpSearchA.Services.Mail.CrmRules.SilentArtists(db);
        Console.WriteLine($"  молчуны: {string.Join(", ", silent.Select(id => db.Artists.First(a => a.Id == id).Email))} (ждём a3)");
    }
    vm.Open(beatB, ids);
    foreach (var r in vm.Recipients)
        Console.WriteLine($"  в рассылке {r.Email}: выбран={r.IsSelected} {r.Warning}");

    MessedUpSearchA.Services.SecretStore.Delete(key);
    if (File.Exists(MessedUpSearchA.Services.AppLog.FilePath))
        Console.WriteLine("лог: " + string.Join(" | ", File.ReadLines(MessedUpSearchA.Services.AppLog.FilePath).Where(l => l.Contains("ответ") || l.Contains("рассылк"))));
    return 0;
}

// «Оживить» на заглушке API (127.0.0.1:8099): MlCheck llm <папка данных-копия>
if (args.Length == 2 && args[0] == "llm")
{
    Environment.SetEnvironmentVariable(MessedUpSearchA.Data.AppPaths.DataDirVariable, args[1]);
    const string url = "http://127.0.0.1:8099/v1";

    try { await new MessedUpSearchA.Services.Llm.LlmClient(url, "bad").ListModelsAsync(); }
    catch (MessedUpSearchA.Services.Llm.LlmException ex) { Console.WriteLine($"плохой ключ: auth={ex.IsAuth} «{ex.Message}»"); }

    var models = await new MessedUpSearchA.Services.Llm.LlmClient(url, "good").ListModelsAsync();
    Console.WriteLine($"модели: {string.Join(", ", models)} -> по умолчанию {MessedUpSearchA.Services.Llm.LlmProviders.PickDefault(models)}");

    using (var db = new MessedUpSearchA.Data.AppDbContext())
    {
        Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.Migrate(db.Database);
        foreach (var a in db.Artists) a.Email = a.Nickname.Replace(" ", "").ToLowerInvariant() + "@ok.test";
        foreach (var b in db.Beats) b.ShareUrl = "https://example.com/beat";
        db.SaveChanges();
    }

    var settings = new MessedUpSearchA.Data.AppSettings { MailAddress = "me@x.test", SmtpHost = "h", MailSenderName = "fewvar" };
    var queue = new MessedUpSearchA.Services.Mail.MailQueue(() => settings);
    var vm = new MessedUpSearchA.ViewModels.MailViewModel(queue, () => settings,
        () => new MessedUpSearchA.Services.Llm.LlmClient(url, "good", "stub-small"));
    using (var db = new MessedUpSearchA.Data.AppDbContext())
        vm.Open(db.Beats.First().Id, db.Artists.Where(a => new[] { "Yopz", "LDE Whyte", "sunraw" }.Contains(a.Nickname)).Select(a => a.Id).ToList());
    vm.SelectedTemplate = vm.Templates.First(t => t.Name.Contains("RU"));

    await vm.PersonalizeCommand.ExecuteAsync(null);
    Console.WriteLine($"статус: {vm.PersonalizeStatus}");
    foreach (var r in vm.Recipients)
        Console.WriteLine($"  {r.Nickname} (трек «{r.TopTrack}»): переписано={r.IsPersonalized} {r.CustomSubject} | {r.CustomBody.Split('\n')[0]}");

    vm.PreviewRecipient = vm.Recipients.First(r => r.IsPersonalized);
    Console.WriteLine($"предпросмотр {vm.PreviewRecipient.Nickname}: {vm.PreviewSubject}");
    vm.EditBody += " ";
    Console.WriteLine($"после правки шаблона переписанных: {vm.Recipients.Count(r => r.IsPersonalized)}");
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
