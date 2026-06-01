using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PrintOrder
{
    internal sealed class SumatraPdfInstallation
    {
        public SumatraPdfInstallation(string? executablePath)
        {
            ExecutablePath = string.IsNullOrWhiteSpace(executablePath)
                ? null
                : executablePath.Trim();
        }

        public string? ExecutablePath { get; }

        public bool IsAvailable => !string.IsNullOrWhiteSpace(ExecutablePath);
    }

    internal static class SumatraPdfSupport
    {
        public const string DownloadUrl = "https://www.sumatrapdfreader.org/download-free-pdf-viewer";

        public static SumatraPdfInstallation Detect()
        {
            foreach (var candidate in EnumerateCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    var fullPath = Path.GetFullPath(candidate.Trim('"'));
                    if (File.Exists(fullPath))
                    {
                        return new SumatraPdfInstallation(fullPath);
                    }
                }
                catch
                {
                    // Abaikan candidate yang tidak bisa dinormalisasi.
                }
            }

            return new SumatraPdfInstallation(null);
        }

        public static string? ResolveExecutablePath()
        {
            return Detect().ExecutablePath;
        }

        public static void OpenDownloadPage()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DownloadUrl,
                UseShellExecute = true
            });
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            foreach (var candidate in EnumerateRegistryCandidates())
            {
                yield return candidate;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            yield return Path.Combine(programFilesX86, "SumatraPDF", "SumatraPDF.exe");
            yield return Path.Combine(programFiles, "SumatraPDF", "SumatraPDF.exe");
            yield return Path.Combine(localAppData, "SumatraPDF", "SumatraPDF.exe");
            yield return Path.Combine(localAppData, "Programs", "SumatraPDF", "SumatraPDF.exe");

            foreach (var candidate in EnumeratePathCandidates())
            {
                yield return candidate;
            }
        }

        private static IEnumerable<string> EnumerateRegistryCandidates()
        {
            var hives = new[]
            {
                RegistryHive.CurrentUser,
                RegistryHive.LocalMachine
            };

            var views = new[]
            {
                RegistryView.Registry64,
                RegistryView.Registry32
            };

            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    using var baseKey = TryOpenBaseKey(hive, view);
                    if (baseKey == null)
                    {
                        continue;
                    }

                    foreach (var candidate in EnumerateAppPathCandidates(baseKey))
                    {
                        yield return candidate;
                    }

                    foreach (var candidate in EnumerateUninstallCandidates(baseKey))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        private static RegistryKey? TryOpenBaseKey(RegistryHive hive, RegistryView view)
        {
            try
            {
                return RegistryKey.OpenBaseKey(hive, view);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<string> EnumerateAppPathCandidates(RegistryKey baseKey)
        {
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\SumatraPDF.exe");
            if (key == null)
            {
                yield break;
            }

            if (key.GetValue(null) is string executablePath)
            {
                yield return executablePath;
            }

            if (key.GetValue("Path") is string installPath)
            {
                yield return Path.Combine(installPath, "SumatraPDF.exe");
            }
        }

        private static IEnumerable<string> EnumerateUninstallCandidates(RegistryKey baseKey)
        {
            using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstallKey == null)
            {
                yield break;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);
                if (subKey == null)
                {
                    continue;
                }

                var displayName = subKey.GetValue("DisplayName") as string;
                if (displayName == null || !displayName.Contains("SumatraPDF", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (subKey.GetValue("InstallLocation") is string installLocation)
                {
                    yield return Path.Combine(installLocation, "SumatraPDF.exe");
                }

                if (subKey.GetValue("DisplayIcon") is string displayIcon)
                {
                    yield return displayIcon.Split(',')[0].Trim('"');
                }
            }
        }

        private static IEnumerable<string> EnumeratePathCandidates()
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return Path.Combine(directory, "SumatraPDF.exe");
            }
        }
    }
}
