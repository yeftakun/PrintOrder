using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

namespace PrintForm
{
    internal static class Program
    {
        private const string CreateRequiredFilesArgument = "--create-required-files";

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            if (!EnsureRequiredFilesOnStartup(args))
            {
                return;
            }

            Application.Run(new Form1());
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
                var confirmationMessage =
                    "Aplikasi membutuhkan file berikut sebelum dijalankan:" + Environment.NewLine
                    + string.Join(Environment.NewLine, missingFiles.Select(file => $"- {file}")) + Environment.NewLine + Environment.NewLine
                    + $"Lokasi: {AppConfig.StorageDirectoryPath}" + Environment.NewLine + Environment.NewLine
                    + "Pilih Yes untuk membuat file tersebut. Aplikasi akan meminta izin Run as administrator.";

                var confirmation = MessageBox.Show(
                    confirmationMessage,
                    "Konfirmasi Pembuatan File",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirmation != DialogResult.Yes)
                {
                    return false;
                }

                if (!IsRunningAsAdministrator())
                {
                    var elevated = RelaunchAsAdministrator();
                    if (!elevated)
                    {
                        MessageBox.Show(
                            "Aplikasi tidak bisa dijalankan sebagai administrator. Pembuatan file dibatalkan.",
                            "Peringatan",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }

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
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = processPath,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = CreateRequiredFilesArgument
                };

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

        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}