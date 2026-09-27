using Microsoft.Data.Sqlite;

namespace Crawler;

/// <summary>
/// Состояние краулера в одной SQLite: кого нашли, что послушали, какие векторы.
/// Всё пишется сразу, поэтому Ctrl+C, сон мака или отвал сети ничего не теряют —
/// следующий запуск продолжает с того же места.
/// </summary>
public sealed class CrawlDb : IDisposable
{
    private readonly SqliteConnection _connection;

    // Одно подключение на два потока (качалка и счётчик) — Microsoft.Data.Sqlite
    // это не переносит, поэтому все записи идут под замком.
    private readonly object _gate = new();

    public CrawlDb(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Pooling=False: базу параллельно пишет Python (separate_embed.py) — держать
        // подключение в пуле после Dispose незачем. Таймаут — на время его записи.
        _connection = new SqliteConnection($"Data Source={path};Pooling=False;Default Timeout=30");
        _connection.Open();

        Execute("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS artists (
                id INTEGER PRIMARY KEY,
                platform TEXT NOT NULL,
                source_id TEXT NOT NULL,
                source_url TEXT NOT NULL UNIQUE,
                nickname TEXT NOT NULL,
                avatar_url TEXT NOT NULL DEFAULT '',
                plays INTEGER NOT NULL DEFAULT 0,
                followers INTEGER NOT NULL DEFAULT 0,
                genre TEXT NOT NULL DEFAULT '',
                producer_reason TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'new',
                found_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tracks (
                id INTEGER PRIMARY KEY,
                artist_id INTEGER NOT NULL REFERENCES artists(id),
                source_track_id TEXT NOT NULL DEFAULT '',
                url TEXT NOT NULL DEFAULT '',
                title TEXT NOT NULL DEFAULT '',
                plays INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'new',
                error TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS vectors (
                track_id INTEGER PRIMARY KEY REFERENCES tracks(id),
                vector BLOB NOT NULL,
                seconds REAL NOT NULL,
                kind TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_tracks_artist ON tracks(artist_id);
            -- Два вектора на кусок (ml/scripts/separate_embed.py): 'full' — как есть, с голосом,
            -- для фильтра «рэпер или нет»; 'inst' — после Demucs, для поиска «бит -> артист».
            -- Таблица vectors — старый конвейер (C#, без разделения), остаётся для истории.
            CREATE TABLE IF NOT EXISTS embeddings (
                track_id INTEGER NOT NULL REFERENCES tracks(id),
                kind TEXT NOT NULL,
                vector BLOB NOT NULL,
                seconds REAL NOT NULL,
                PRIMARY KEY (track_id, kind)
            );
            """);
    }

    public SqliteConnection Connection => _connection;

    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, parameters);
            return command.ExecuteNonQuery();
        }
    }

    public T? Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var command = Command(sql, parameters);
            var value = command.ExecuteScalar();
            return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
        }
    }

    public SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public void Dispose() => _connection.Dispose();
}
