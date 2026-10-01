using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MessedUpSearchA.Services;

/// <summary>
/// Пароли почты и ключи нейросетей — в системном хранилище, а не в settings.json:
/// macOS Keychain и Windows Credential Manager. Файл настроек лежит открытым текстом
/// и уходит в бэкапы, связка ключей — зашифрована и привязана к пользователю.
///
/// Ключ записи — «smtp:адрес», «llm:провайдер». Чтение несуществующего — null.
/// </summary>
public static class SecretStore
{
    private const string Service = "MessedUpSearch";

    public static string? Get(string key)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) return MacKeychain.Get(Service, key);
            if (OperatingSystem.IsWindows()) return WindowsCredentials.Get($"{Service}:{key}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"хранилище паролей, чтение {key}: {ex.Message}");
        }

        return null;
    }

    /// <returns>false — сохранить не вышло (нет хранилища или система отказала).</returns>
    public static bool Set(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Delete(key);
            return true;
        }

        try
        {
            if (OperatingSystem.IsMacOS()) return MacKeychain.Set(Service, key, value);
            if (OperatingSystem.IsWindows()) return WindowsCredentials.Set($"{Service}:{key}", value);
        }
        catch (Exception ex)
        {
            AppLog.Write($"хранилище паролей, запись {key}: {ex.Message}");
        }

        return false;
    }

    public static void Delete(string key)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) MacKeychain.Delete(Service, key);
            else if (OperatingSystem.IsWindows()) WindowsCredentials.Delete($"{Service}:{key}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"хранилище паролей, удаление {key}: {ex.Message}");
        }
    }

    /// <summary>
    /// Связка ключей через старый API SecKeychain*: он проще SecItem (без словарей CoreFoundation)
    /// и работает во всех версиях macOS, хоть и помечен устаревшим.
    /// </summary>
    private static class MacKeychain
    {
        private const string Security = "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const int ItemNotFound = -25300;

        [DllImport(Security)]
        private static extern int SecKeychainAddGenericPassword(IntPtr keychain,
            uint serviceLength, byte[] service, uint accountLength, byte[] account,
            uint passwordLength, byte[] password, IntPtr itemRef);

        [DllImport(Security)]
        private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray,
            uint serviceLength, byte[] service, uint accountLength, byte[] account,
            out uint passwordLength, out IntPtr password, out IntPtr itemRef);

        [DllImport(Security)]
        private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList,
            uint length, byte[] data);

        [DllImport(Security)]
        private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

        [DllImport(Security)]
        private static extern int SecKeychainItemDelete(IntPtr itemRef);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);

        public static string? Get(string service, string account)
        {
            var (s, a) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account));
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
                out var length, out var data, out var item);
            if (status == ItemNotFound)
                return null;
            Check(status, "find");

            try
            {
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, (int)length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                if (item != IntPtr.Zero) CFRelease(item);
            }
        }

        public static bool Set(string service, string account, string value)
        {
            var (s, a, v) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account), Encoding.UTF8.GetBytes(value));

            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
                out _, out var data, out var item);
            if (status == 0)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                try
                {
                    Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)v.Length, v), "modify");
                }
                finally
                {
                    CFRelease(item);
                }
                return true;
            }

            Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
                (uint)v.Length, v, IntPtr.Zero), "add");
            return true;
        }

        public static void Delete(string service, string account)
        {
            var (s, a) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account));
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a,
                out _, out var data, out var item);
            if (status != 0)
                return;

            SecKeychainItemFreeContent(IntPtr.Zero, data);
            try
            {
                Check(SecKeychainItemDelete(item), "delete");
            }
            finally
            {
                CFRelease(item);
            }
        }

        private static void Check(int status, string what)
        {
            if (status != 0)
                throw new InvalidOperationException($"Keychain {what}: OSStatus {status}");
        }
    }

    /// <summary>Диспетчер учётных данных Windows: общие (generic) записи текущего пользователя.</summary>
    private static class WindowsCredentials
    {
        private const uint Generic = 1;
        private const uint PersistLocalMachine = 2;
        private const int NotFound = 1168;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static string? Get(string target)
        {
            if (!CredRead(target, Generic, 0, out var pointer))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == NotFound)
                    return null;
                throw new InvalidOperationException($"CredRead: {error}");
            }

            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                if (credential.CredentialBlobSize == 0)
                    return string.Empty;
                return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        public static bool Set(string target, string value)
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new Credential
                {
                    Type = Generic,
                    TargetName = target,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = PersistLocalMachine,
                    UserName = Environment.UserName
                };
                if (!CredWrite(ref credential, 0))
                    throw new InvalidOperationException($"CredWrite: {Marshal.GetLastWin32Error()}");
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(blob);
            }
        }

        public static void Delete(string target)
        {
            if (!CredDelete(target, Generic, 0) && Marshal.GetLastWin32Error() != NotFound)
                throw new InvalidOperationException($"CredDelete: {Marshal.GetLastWin32Error()}");
        }
    }
}
