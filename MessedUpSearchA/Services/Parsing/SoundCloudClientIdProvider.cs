using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MessedUpSearchA.Data;

namespace MessedUpSearchA.Services.Parsing;

/// <summary>
/// SoundCloud не выдаёт ключи с 2017 года, поэтому client_id берётся из JS-бандла сайта.
/// Значение ротируется раз в несколько дней — кешируем на двое суток и обновляем при отказе.
/// </summary>
public class SoundCloudClientIdProvider
{
    private static readonly Regex ScriptSrc =
        new(@"src=""(https://a-v2\.sndcdn\.com/assets/[^""]+\.js)""", RegexOptions.Compiled);

    private static readonly Regex ClientId =
        new(@"client_id[:=]""([a-zA-Z0-9]{32})""", RegexOptions.Compiled);

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(48);

    private readonly Func<string, CancellationToken, Task<string>> _fetch;
    private readonly string _cacheFile;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string _current = string.Empty;

    public SoundCloudClientIdProvider(
        Func<string, CancellationToken, Task<string>>? fetch = null,
        string? cacheFile = null)
    {
        _fetch = fetch ?? ParsingHttp.GetStringAsync;
        _cacheFile = cacheFile ?? Path.Combine(AppPaths.DataDir, "soundcloud-client-id.json");
    }

    public async Task<string> GetAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_current))
            return _current;

        await _gate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_current))
                return _current;

            var cached = ReadCache();
            if (cached is not null)
            {
                _current = cached;
                return _current;
            }

            _current = await ExtractAsync(ct);
            WriteCache(_current);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Вызывать при 401/403 — заставляет вытащить свежий id со страницы.</summary>
    public async Task<string> RefreshAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _current = await ExtractAsync(ct);
            WriteCache(_current);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ExtractAsync(CancellationToken ct)
    {
        var page = await _fetch("https://soundcloud.com/", ct);

        var scripts = ScriptSrc.Matches(page)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Reverse()
            .ToList();

        if (scripts.Count == 0)
            throw new InvalidOperationException("не нашёл ссылок на скрипты на странице SoundCloud");

        foreach (var script in scripts)
        {
            ct.ThrowIfCancellationRequested();

            string body;
            try
            {
                body = await _fetch(script, ct);
            }
            catch
            {
                continue;
            }

            var match = ClientId.Match(body);
            if (match.Success)
                return match.Groups[1].Value;
        }

        throw new InvalidOperationException(
            "client_id не найден в скриптах SoundCloud — вероятно, они поменяли структуру бандла");
    }

    private string? ReadCache()
    {
        try
        {
            if (!File.Exists(_cacheFile))
                return null;

            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(_cacheFile));

            if (entry is null || string.IsNullOrWhiteSpace(entry.Value))
                return null;

            return DateTime.UtcNow - entry.FetchedAt < Ttl ? entry.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private void WriteCache(string value)
    {
        try
        {
            File.WriteAllText(_cacheFile, JsonSerializer.Serialize(
                new CacheEntry { Value = value, FetchedAt = DateTime.UtcNow }));
        }
        catch
        {

        }
    }

    private class CacheEntry
    {
        public string Value { get; set; } = string.Empty;
        public DateTime FetchedAt { get; set; }
    }
}
