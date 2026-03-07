using System;
using System.IO;
using System.Text;

namespace PrintForm
{
    internal static class AppConfig
    {
        private const string DefaultServerBaseUrl = "http://127.0.0.1:3000";
        private const string ConfigFileName = "printform.ini";
        private const string ClientIdFileName = "printform.client-id";

        public static string LoadServerBaseUrl()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
            EnsureConfigExists(configPath);

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
            var clientIdPath = Path.Combine(AppContext.BaseDirectory, ClientIdFileName);

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

                var generated = Guid.NewGuid().ToString("D");
                File.WriteAllText(clientIdPath, generated + Environment.NewLine, new UTF8Encoding(false));
                return generated;
            }
            catch
            {
                // Fallback: tetap punya ID walau gagal persist.
                return Guid.NewGuid().ToString("D");
            }
        }

        private static void EnsureConfigExists(string configPath)
        {
            if (File.Exists(configPath))
            {
                return;
            }

            var content = string.Join(Environment.NewLine, new[]
            {
                "; PrintForm configuration",
                "; Ubah base_url sesuai alamat server",
                "[server]",
                $"base_url={DefaultServerBaseUrl}",
                string.Empty
            });

            try
            {
                File.WriteAllText(configPath, content, new UTF8Encoding(false));
            }
            catch
            {
                // Abaikan jika file tidak bisa dibuat.
            }
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
