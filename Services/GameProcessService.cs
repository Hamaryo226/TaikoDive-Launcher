using System.Diagnostics;
using TaikoDiveLauncher.Models;

namespace TaikoDiveLauncher.Services;

public static class GameProcessService
{
    public static bool IsRunning()
    {
        Process[] processes = Process.GetProcessesByName("TaikoDive");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static OperationResult Launch(TaikoDiveInstallation installation)
    {
        if (!installation.IsValid)
        {
            return OperationResult.Failure("TaikoDive.exe が見つかりません。ゲームフォルダーを選び直してください。");
        }

        if (IsRunning())
        {
            return OperationResult.Failure("TaikoDive はすでに起動しています。");
        }

        try
        {
            Process? process = Process.Start(new ProcessStartInfo(installation.ExecutablePath)
            {
                WorkingDirectory = installation.BuildDirectory,
                UseShellExecute = true,
            });

            if (process is null)
            {
                return OperationResult.Failure("TaikoDive を起動できませんでした。");
            }

            process.Dispose();
            return OperationResult.Success("TaikoDive を起動しました。");
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"TaikoDive を起動できませんでした: {ex.Message}");
        }
    }
}
