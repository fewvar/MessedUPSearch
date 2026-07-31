using System;
using System.IO;

namespace MessedUpSearchA.Data;

public static class AppPaths
{

    public const string DataDirVariable = "MESSEDUP_DATA_DIR";

    public static string DataDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable(DataDirVariable);

            var dir = string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MessedUpSearch")
                : custom;

            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DbFile => Path.Combine(DataDir, "app.db");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}
