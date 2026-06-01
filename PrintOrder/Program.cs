using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace PrintOrder
{
    internal static class Program
    {
        private const string CreateRequiredFilesArgument = "--create-required-files";
        private const string SaveServerBaseUrlArgument = "--save-server-base-url";
        private const string SingleInstanceMutexName = "Local\\PrintOrder.SingleInstance";

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (TryHandleCommandLineActions(args))
            {
                return;
            }

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            if (!EnsureRequiredFilesOnStartup(args))
            {
                return;
            }

            using var singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                MessageBox.Show(
                    "PrintOrder sudah berjalan. Tutup aplikasi yang sedang aktif sebelum membuka lagi.",
                    "PrintOrder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.Run(new Form1());
        }

        internal static bool TrySaveServerBaseUrl(string baseUrl, out string errorMessage)
        {
            errorMessage = string.Empty;

            var normalized = (baseUrl ?? string.Empty).Trim().Trim('"').TrimEnd('/');
            if (!AppConfig.IsValidServerBaseUrl(normalized))
            {
                errorMessage = "Nilai base_url tidak valid. Gunakan URL HTTP/HTTPS yang lengkap.";
                return false;
            }

            try
            {
                AppConfig.SaveServerBaseUrl(normalized);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = $"Tidak punya izin menulis printorder.ini di {AppConfig.GetConfigFilePath()}.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Gagal menyimpan file printorder.ini: {ex.Message}";
                return false;
            }
        }

        private static bool TryHandleCommandLineActions(string[] args)
        {
            if (!TryGetArgumentValue(args, SaveServerBaseUrlArgument, out var baseUrl))
            {
                return false;
            }

            try
            {
                AppConfig.SaveServerBaseUrl(baseUrl);
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }

            return true;
        }

        private static bool TryGetArgumentValue(string[] args, string argumentName, out string value)
        {
            value = string.Empty;

            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    return false;
                }

                value = args[i + 1];
                return true;
            }

            return false;
        }

        private static bool EnsureRequiredFilesOnStartup(string[] args)
        {
            var missingFiles = AppConfig.GetMissingRequiredFiles();
            if (missingFiles.Length == 0)
            {
                return true;
            }

            var createWithoutPrompt = args.Any(arg => string.Equals(arg, CreateRequiredFilesArgument, StringComparison.OrdinalIgnoreCase));

            if (!createWithoutPrompt)
            {
                var missingFileLines = missingFiles.Select(file =>
                {
                    var filePath = AppConfig.GetRequiredFilePath(file);
                    return string.IsNullOrWhiteSpace(filePath)
                        ? $"- {file}"
                        : $"- {file} ({filePath})";
                });

                var confirmationMessage =
                    "Aplikasi membutuhkan file berikut sebelum dijalankan:" + Environment.NewLine
                    + string.Join(Environment.NewLine, missingFileLines) + Environment.NewLine + Environment.NewLine
                    + "Pilih Yes untuk membuat file tersebut.";

                var confirmation = MessageBox.Show(
                    confirmationMessage,
                    "Konfirmasi Pembuatan File",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirmation != DialogResult.Yes)
                {
                    return false;
                }
            }

            try
            {
                var createdFiles = AppConfig.CreateMissingRequiredFiles();
                var stillMissing = AppConfig.GetMissingRequiredFiles();

                if (stillMissing.Length > 0)
                {
                    MessageBox.Show(
                        "Gagal membuat file berikut:" + Environment.NewLine
                        + string.Join(Environment.NewLine, stillMissing.Select(file => $"- {file}")),
                        "Gagal Membuat File",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return false;
                }

                if (createdFiles.Length > 0)
                {
                    MessageBox.Show(
                        "Berhasil membuat file berikut:" + Environment.NewLine
                        + string.Join(Environment.NewLine, createdFiles.Select(file => $"- {file}")),
                        "Pembuatan File Berhasil",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                if (!IsRunningAsAdministrator())
                {
                    var elevated = RelaunchAsAdministrator();
                    if (elevated)
                    {
                        return false;
                    }
                }

                MessageBox.Show(
                    "Tidak punya izin untuk membuat file konfigurasi. Jalankan sebagai administrator atau ubah lokasi folder aplikasi.",
                    "Gagal Membuat File",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Terjadi kesalahan saat membuat file konfigurasi:" + Environment.NewLine + ex.Message,
                    "Gagal Membuat File",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }
        }

        private static bool RelaunchAsAdministrator()
        {
            try
            {
                var startInfo = CreateSelfStartInfo(true, CreateRequiredFilesArgument);
                if (startInfo == null)
                {
                    return false;
                }

                Process.Start(startInfo);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User membatalkan prompt UAC.
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static ProcessStartInfo? CreateSelfStartInfo(bool runAsAdministrator, params string[] appArguments)
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = true
            };

            if (runAsAdministrator)
            {
                startInfo.Verb = "runas";
            }

            // Saat dijalankan via `dotnet run`, process utama adalah dotnet host.
            // Kita perlu meneruskan path dll app agar relaunch tetap masuk ke aplikasi ini.
            var commandLineArgs = Environment.GetCommandLineArgs();
            if (IsDotNetHost(processPath)
                && commandLineArgs.Length > 0
                && commandLineArgs[0].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(commandLineArgs[0]);
            }

            foreach (var argument in appArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        private static bool IsDotNetHost(string processPath)
        {
            return string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
