using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PrintForm
{
    internal static class AppConfig
    {
        private const string DefaultServerBaseUrl = "http://127.0.0.1:3000";
        private const string StorageFolderName = "PrintForm";
        private const string ConfigFileName = "printform.ini";
        private const string ClientIdFileName = "printform.client-id";
        private const string AuthStateFileName = "printform.auth.json";

        private static readonly string LegacyStorageDirectoryPath = AppContext.BaseDirectory;
        private static readonly string CurrentUserStorageDirectoryPath = ResolveUserStorageDirectoryPath();
        private static readonly string CurrentMachineStorageDirectoryPath = ResolveMachineStorageDirectoryPath();

        public static string StorageDirectoryPath => CurrentUserStorageDirectoryPath;
        public static string ClientIdStorageDirectoryPath => CurrentMachineStorageDirectoryPath;

        public static string[] GetMissingRequiredFiles()
        {
            TryMigrateLegacyFiles();

            var missing = new List<string>();

            if (!File.Exists(GetConfigFilePath()))
            {
                missing.Add(ConfigFileName);
            }

            if (!File.Exists(GetClientIdFilePath()))
            {
                missing.Add(ClientIdFileName);
            }

            return missing.ToArray();
        }

        public static string[] CreateMissingRequiredFiles()
        {
            Directory.CreateDirectory(CurrentUserStorageDirectoryPath);
            Directory.CreateDirectory(CurrentMachineStorageDirectoryPath);

            var created = new List<string>();
            var configPath = GetConfigFilePath();
            var clientIdPath = GetClientIdFilePath();

            if (!File.Exists(configPath))
            {
                WriteDefaultConfig(configPath);
                created.Add(ConfigFileName);
            }

            if (!File.Exists(clientIdPath))
            {
                WriteNewClientId(clientIdPath);
                created.Add(ClientIdFileName);
            }

            return created.ToArray();
        }

        public static bool IsValidServerBaseUrl(string value)
        {
            return IsValidBaseUrl(value);
        }

        public static void SaveServerBaseUrl(string baseUrl)
        {
            SaveAppSettings(baseUrl, LoadNotificationOptions());
        }

        public static void SaveAppSettings(string baseUrl, NotificationOptions notificationOptions)
        {
            var normalized = (baseUrl ?? string.Empty).Trim().Trim('"').TrimEnd('/');
            if (!IsValidBaseUrl(normalized))
            {
                throw new ArgumentException("Nilai base_url tidak valid.", nameof(baseUrl));
            }

            notificationOptions ??= new NotificationOptions();

            Directory.CreateDirectory(CurrentUserStorageDirectoryPath);

            var configPath = GetConfigFilePath();
            var content = BuildConfigContent(normalized, notificationOptions);
            File.WriteAllText(configPath, content, new UTF8Encoding(false));
        }

        public static NotificationOptions LoadNotificationOptions()
        {
            TryMigrateLegacyFiles();

            var options = new NotificationOptions();
            var configPath = GetConfigFilePath();

            try
            {
                if (!File.Exists(configPath))
                {
                    return options;
                }

                foreach (var rawLine in File.ReadAllLines(configPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 1)..].Trim().Trim('"');

                    if ((string.Equals(key, "sound_enabled", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(key, "sound_notification_enabled", StringComparison.OrdinalIgnoreCase))
                        && TryParseConfigBoolean(value, out var soundEnabled))
                    {
                        options.SoundEnabled = soundEnabled;
                    }

                    if ((string.Equals(key, "notification_enabled", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(key, "desktop_enabled", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(key, "desktop_notification_enabled", StringComparison.OrdinalIgnoreCase))
                        && TryParseConfigBoolean(value, out var desktopEnabled))
                    {
                        options.DesktopEnabled = desktopEnabled;
                    }
                }
            }
            catch
            {
                // Pakai default jika gagal membaca konfigurasi.
            }

            return options;
        }

        public static string GetConfigFilePath()
        {
            return Path.Combine(CurrentUserStorageDirectoryPath, ConfigFileName);
        }

        public static string GetClientIdFilePath()
        {
            return Path.Combine(CurrentMachineStorageDirectoryPath, ClientIdFileName);
        }

        public static string GetAuthStateFilePath()
        {
            return Path.Combine(CurrentUserStorageDirectoryPath, AuthStateFileName);
        }

        public static string? GetRequiredFilePath(string fileName)
        {
            if (string.Equals(fileName, ConfigFileName, StringComparison.OrdinalIgnoreCase))
            {
                return GetConfigFilePath();
            }

            if (string.Equals(fileName, ClientIdFileName, StringComparison.OrdinalIgnoreCase))
            {
                return GetClientIdFilePath();
            }

            if (string.Equals(fileName, AuthStateFileName, StringComparison.OrdinalIgnoreCase))
            {
                return GetAuthStateFilePath();
            }

            return null;
        }

        public static string LoadServerBaseUrl()
        {
            TryMigrateLegacyFiles();
            var configPath = GetConfigFilePath();

            try
            {
                foreach (var rawLine in File.ReadAllLines(configPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    if (!string.Equals(key, "base_url", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = line[(separatorIndex + 1)..].Trim().Trim('"');
                    if (IsValidBaseUrl(value))
                    {
                        return value.TrimEnd('/');
                    }

                    return DefaultServerBaseUrl;
                }
            }
            catch
            {
                // Abaikan kesalahan baca file konfigurasi.
            }

            return DefaultServerBaseUrl;
        }

        public static string LoadOrCreateClientId()
        {
            TryMigrateLegacyFiles();
            var clientIdPath = GetClientIdFilePath();

            try
            {
                if (File.Exists(clientIdPath))
                {
                    var existing = File.ReadAllText(clientIdPath).Trim();
                    if (TryNormalizeGuid(existing, out var normalized))
                    {
                        return normalized;
                    }
                }

                var generated = WriteNewClientId(clientIdPath);
                return generated;
            }
            catch
            {
                // Fallback: tetap punya ID walau gagal persist.
                return Guid.NewGuid().ToString("D");
            }
        }

        public static AuthState? LoadAuthState()
        {
            TryMigrateLegacyFiles();
            var authStatePath = GetAuthStateFilePath();

            try
            {
                if (!File.Exists(authStatePath))
                {
                    return null;
                }

                var raw = File.ReadAllText(authStatePath);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                var state = JsonSerializer.Deserialize<AuthState>(raw);
                if (state == null || string.IsNullOrWhiteSpace(state.RefreshToken))
                {
                    return null;
                }

                return new AuthState
                {
                    RefreshToken = state.RefreshToken.Trim(),
                    UserId = string.IsNullOrWhiteSpace(state.UserId) ? null : state.UserId.Trim(),
                    Username = string.IsNullOrWhiteSpace(state.Username) ? null : state.Username.Trim()
                };
            }
            catch
            {
                return null;
            }
        }

        public static void SaveAuthState(AuthState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var refreshToken = (state.RefreshToken ?? string.Empty).Trim();
            if (refreshToken.Length == 0)
            {
                throw new ArgumentException("Refresh token wajib diisi.", nameof(state));
            }

            var normalized = new AuthState
            {
                RefreshToken = refreshToken,
                UserId = string.IsNullOrWhiteSpace(state.UserId) ? null : state.UserId.Trim(),
                Username = string.IsNullOrWhiteSpace(state.Username) ? null : state.Username.Trim()
            };

            var raw = JsonSerializer.Serialize(normalized, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Directory.CreateDirectory(CurrentUserStorageDirectoryPath);
            File.WriteAllText(GetAuthStateFilePath(), raw + Environment.NewLine, new UTF8Encoding(false));
        }

        public static void ClearAuthState()
        {
            var authStatePath = GetAuthStateFilePath();
            try
            {
                if (File.Exists(authStatePath))
                {
                    File.Delete(authStatePath);
                }
            }
            catch
            {
                // Abaikan kegagalan hapus state auth.
            }
        }

        private static void WriteDefaultConfig(string configPath)
        {
            var content = BuildConfigContent(DefaultServerBaseUrl, new NotificationOptions());

            File.WriteAllText(configPath, content, new UTF8Encoding(false));
        }

        private static string BuildConfigContent(string baseUrl, NotificationOptions notificationOptions)
        {
            notificationOptions ??= new NotificationOptions();

            return string.Join(Environment.NewLine, new[]
            {
                "; PrintForm configuration",
                "; Ubah base_url sesuai alamat server",
                "[server]",
                $"base_url={baseUrl}",
                string.Empty,
                "[notification]",
                $"sound_enabled={ToConfigBoolean(notificationOptions.SoundEnabled)}",
                $"notification_enabled={ToConfigBoolean(notificationOptions.DesktopEnabled)}",
                string.Empty
            });
        }

        private static string WriteNewClientId(string clientIdPath)
        {
            var generated = Guid.NewGuid().ToString("D");
            File.WriteAllText(clientIdPath, generated + Environment.NewLine, new UTF8Encoding(false));
            return generated;
        }

        private static bool TryParseConfigBoolean(string value, out bool result)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();

            if (normalized is "1" or "true" or "yes" or "on")
            {
                result = true;
                return true;
            }

            if (normalized is "0" or "false" or "no" or "off")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static string ToConfigBoolean(bool value)
        {
            return value ? "true" : "false";
        }

        private static bool IsValidBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryNormalizeGuid(string value, out string normalized)
        {
            if (Guid.TryParse(value, out var guid))
            {
                normalized = guid.ToString("D");
                return true;
            }

            normalized = string.Empty;
            return false;
        }

        private static string ResolveUserStorageDirectoryPath()
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    var candidate = Path.Combine(localAppData, StorageFolderName);
                    Directory.CreateDirectory(candidate);
                    return candidate;
                }
            }
            catch
            {
                // Fallback ke base directory jika LocalAppData tidak tersedia.
            }

            return LegacyStorageDirectoryPath;
        }

        private static string ResolveMachineStorageDirectoryPath()
        {
            try
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                if (!string.IsNullOrWhiteSpace(programData))
                {
                    var candidate = Path.Combine(programData, StorageFolderName);
                    Directory.CreateDirectory(candidate);
                    return candidate;
                }
            }
            catch
            {
                // Fallback ke LocalAppData jika ProgramData tidak tersedia.
            }

            return CurrentUserStorageDirectoryPath;
        }

        private static void TryMigrateLegacyFiles()
        {
            TryMigrateFile(
                GetConfigFilePath(),
                Path.Combine(LegacyStorageDirectoryPath, ConfigFileName));

            TryMigrateFile(
                GetAuthStateFilePath(),
                Path.Combine(LegacyStorageDirectoryPath, AuthStateFileName));

            // Prioritas migrasi client-id: lokasi user lama lalu lokasi executable lama.
            TryMigrateFile(
                GetClientIdFilePath(),
                Path.Combine(CurrentUserStorageDirectoryPath, ClientIdFileName),
                Path.Combine(LegacyStorageDirectoryPath, ClientIdFileName));
        }

        private static void TryMigrateFile(string destinationPath, params string[] sourcePaths)
        {
            try
            {
                if (File.Exists(destinationPath))
                {
                    return;
                }

                foreach (var sourcePath in sourcePaths)
                {
                    if (string.IsNullOrWhiteSpace(sourcePath) || AreSamePath(sourcePath, destinationPath))
                    {
                        continue;
                    }

                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrWhiteSpace(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    File.Copy(sourcePath, destinationPath, overwrite: false);
                    return;
                }
            }
            catch
            {
                // Abaikan migrasi gagal; aplikasi tetap bisa membuat file baru.
            }
        }

        private static bool AreSamePath(string leftPath, string rightPath)
        {
            try
            {
                var leftFullPath = Path.GetFullPath(leftPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var rightFullPath = Path.GetFullPath(rightPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return string.Equals(leftFullPath, rightFullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
