using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PrintForm
{
    internal static class AppConfig
    {
        private const string DefaultServerBaseUrl = "http://127.0.0.1:3000";
        private const string ConfigFileName = "printform.ini";
        private const string ClientIdFileName = "printform.client-id";

        public static string StorageDirectoryPath => AppContext.BaseDirectory;

        public static string[] GetMissingRequiredFiles()
        {
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
            var normalized = (baseUrl ?? string.Empty).Trim().Trim('"').TrimEnd('/');
            if (!IsValidBaseUrl(normalized))
            {
                throw new ArgumentException("Nilai base_url tidak valid.", nameof(baseUrl));
            }

            var configPath = GetConfigFilePath();
            var content = BuildConfigContent(normalized);
            File.WriteAllText(configPath, content, new UTF8Encoding(false));
        }

        public static string GetConfigFilePath()
        {
            return Path.Combine(StorageDirectoryPath, ConfigFileName);
        }

        public static string GetClientIdFilePath()
        {
            return Path.Combine(StorageDirectoryPath, ClientIdFileName);
        }

        public static string LoadServerBaseUrl()
        {
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

        private static void WriteDefaultConfig(string configPath)
        {
            var content = BuildConfigContent(DefaultServerBaseUrl);

            File.WriteAllText(configPath, content, new UTF8Encoding(false));
        }

        private static string BuildConfigContent(string baseUrl)
        {
            return string.Join(Environment.NewLine, new[]
            {
                "; PrintForm configuration",
                "; Ubah base_url sesuai alamat server",
                "[server]",
                $"base_url={baseUrl}",
                string.Empty
            });
        }

        private static string WriteNewClientId(string clientIdPath)
        {
            var generated = Guid.NewGuid().ToString("D");
            File.WriteAllText(clientIdPath, generated + Environment.NewLine, new UTF8Encoding(false));
            return generated;
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
    }
}
