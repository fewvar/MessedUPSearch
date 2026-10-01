using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MessedUpSearchA.Data;
using MessedUpSearchA.Models;

namespace MessedUpSearchA.Services;

/// <summary>
/// Сессии в DAW — только с согласия. Раз в минуту: какое приложение сейчас на переднем плане
/// и DAW ли это. Пишутся только интервалы «FL Studio 14:05–16:40»: ни списка процессов,
/// ни заголовков окон (на macOS для заголовков нужна «Запись экрана» — её не просим).
/// </summary>
public class DawTracker(Func<AppSettings> settings)
{
    /// <summary>Отошёл за сэмплом в браузер на 10 минут — это та же сессия.</summary>
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);

    private int _sessionId;
    private string _sessionApp = string.Empty;
    private DateTime _lastSeen;

    public void Tick(DateTime now)
    {
        if (!settings().DawTracking)
            return;

        string? daw;
        try
        {
            daw = DawCatalog.Match(Foreground.Current());
        }
        catch (Exception ex)
        {
            AppLog.Write($"DAW: не удалось узнать активное приложение: {ex.Message}");
            return;
        }

        Record(now, daw);
    }

    /// <summary>Отметить минуту: DAW на переднем плане (или нет — тогда ничего не пишется).</summary>
    public void Record(DateTime now, string? daw)
    {
        if (daw is null)
            return;

        using var db = new AppDbContext();
        var stamp = now.ToString("yyyy-MM-dd HH:mm");

        if (_sessionId != 0 && daw == _sessionApp && now - _lastSeen <= Gap &&
            db.DawSessions.FirstOrDefault(s => s.Id == _sessionId) is { } open)
        {
            open.EndedAt = stamp;
        }
        else
        {
            var session = new DawSession { App = daw, StartedAt = stamp, EndedAt = stamp };
            db.DawSessions.Add(session);
            db.SaveChanges();
            _sessionId = session.Id;
            _sessionApp = daw;
        }

        _lastSeen = now;
        db.SaveChanges();
    }

    /// <summary>«Стереть» в настройках.</summary>
    public void EraseAll()
    {
        using var db = new AppDbContext();
        db.DawSessions.RemoveRange(db.DawSessions);
        db.SaveChanges();
        _sessionId = 0;
    }
}

/// <summary>Что на переднем плане: отображаемое имя, bundle id (macOS) или имя процесса (Windows).</summary>
public record ForegroundApp(string Name, string BundleId, string ProcessName);

/// <summary>
/// Список DAW. Имена процессов Windows сверены по daws.json проекта DAWRPC (FL64/FL, «Ableton Live N …»,
/// reaper, «Studio One», «Bitwig Studio», CubaseNN); ProTools и Reason — не сверены. На macOS — по
/// отображаемому имени приложения и bundle id: имя стабильнее между версиями, чем исполняемый файл.
/// Подтверждено на месте: GarageBand — com.apple.garageband10.
/// </summary>
public static class DawCatalog
{
    private record Daw(string Name, string[] BundlePrefixes, Regex MacName, Regex WinProcess);

    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Daw[] All =
    [
        new("FL Studio", ["com.image-line."], R(@"^FL Studio"), R(@"^FL(64)?$")),
        new("Ableton Live", ["com.ableton.live"], R(@"^Ableton Live"), R(@"^Ableton Live")),
        new("Logic Pro", ["com.apple.logic"], R(@"^Logic Pro"), R(@"(?!)")),
        new("GarageBand", ["com.apple.garageband"], R(@"^GarageBand"), R(@"(?!)")),
        new("Reaper", ["com.cockos.reaper"], R(@"^REAPER"), R(@"^reaper$")),
        new("Studio One", ["com.presonus.", "com.fender.studio"], R(@"^(Studio One|Studio Pro)"), R(@"^Studio (One|Pro)$")),
        new("Bitwig Studio", ["com.bitwig."], R(@"^Bitwig Studio"), R(@"^Bitwig Studio$")),
        new("Cubase", ["com.steinberg.cubase"], R(@"^Cubase"), R(@"^Cubase ?\d*$")),
        new("Pro Tools", ["com.avid.protools"], R(@"^Pro Tools"), R(@"^ProTools$")),
        new("Reason", ["com.propellerheads.reason", "com.reasonstudios."], R(@"^Reason( \d+)?$"), R(@"^Reason$")),
    ];

    public static string? Match(ForegroundApp? app)
    {
        if (app is null)
            return null;

        foreach (var daw in All)
        {
            if (OperatingSystem.IsMacOS())
            {
                if (daw.BundlePrefixes.Any(p => app.BundleId.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
                    daw.MacName.IsMatch(app.Name))
                    return daw.Name;
            }
            else if (daw.WinProcess.IsMatch(app.ProcessName))
            {
                return daw.Name;
            }
        }

        return null;
    }
}

/// <summary>Активное приложение. Только имя и id — без заголовка окна и без списка остальных процессов.</summary>
public static class Foreground
{
    public static ForegroundApp? Current()
    {
        if (OperatingSystem.IsMacOS()) return Mac.Frontmost();
        if (OperatingSystem.IsWindows()) return Win.Foreground();
        return null;
    }

    /// <summary>NSWorkspace.sharedWorkspace.frontmostApplication — разрешений не требует.</summary>
    private static class Mac
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        public static ForegroundApp? Frontmost()
        {
            var workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var app = Send(workspace, sel_registerName("frontmostApplication"));
            if (app == IntPtr.Zero)
                return null;

            return new ForegroundApp(
                Text(Send(app, sel_registerName("localizedName"))),
                Text(Send(app, sel_registerName("bundleIdentifier"))),
                string.Empty);
        }

        private static string Text(IntPtr nsString) =>
            nsString == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUTF8(Send(nsString, sel_registerName("UTF8String"))) ?? string.Empty;
    }

    /// <summary>Процесс окна на переднем плане. Заголовок окна не читается.</summary>
    private static class Win
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        public static ForegroundApp? Foreground()
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
                return null;

            GetWindowThreadProcessId(window, out var pid);
            using var process = Process.GetProcessById((int)pid);
            return new ForegroundApp(string.Empty, string.Empty, process.ProcessName);
        }
    }
}
