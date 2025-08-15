using System.Diagnostics;

namespace UsnJrnl2Evtx;

public static class ServiceInstaller
{
    public static void Install(string serviceName, string displayName, string description)
    {
        if (ServiceExists(serviceName))
        {
            Console.WriteLine($"Service '{serviceName}' already exists. Skipping create.");
        }
        else
        {
            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName
                             ?? throw new InvalidOperationException("Cannot determine executable path.");
            // sc requires spaces after '='; keep quoting for paths with spaces
            RunSc($"create \"{serviceName}\" binPath= \"{exePath}\" start= auto DisplayName= \"{displayName}\" obj= LocalSystem");
            RunSc($"description \"{serviceName}\" \"{description}\"");
        }

        // Optional: set recovery options (restart on failure)
        // Restart after 5 seconds, reset fail count after 1 day (86400 sec)
        RunSc($"failure \"{serviceName}\" reset= 86400 actions= restart/5000");
    }

    public static void Uninstall(string serviceName)
    {
        if (!ServiceExists(serviceName))
        {
            Console.WriteLine($"Service '{serviceName}' does not exist.");
            return;
        }

        // Try stop, ignore failures if already stopped
        RunSc($"stop \"{serviceName}\"", ignoreErrors: true);
        Thread.Sleep(1000);
        RunSc($"delete \"{serviceName}\"");
    }

    private static bool ServiceExists(string serviceName)
    {
        var (exitCode, _, _) = RunSc($"query \"{serviceName}\"", ignoreErrors: true, captureOutput: true);
        return exitCode == 0;
    }

    private static (int exitCode, string stdOut, string stdErr) RunSc(string args, bool ignoreErrors = false, bool captureOutput = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)!;
        string stdout = captureOutput ? proc.StandardOutput.ReadToEnd() : string.Empty;
        string stderr = captureOutput ? proc.StandardError.ReadToEnd() : string.Empty;
        proc.WaitForExit();

        if (!ignoreErrors && proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"sc.exe {args} failed with code {proc.ExitCode}. stderr: {stderr}");
        }

        return (proc.ExitCode, stdout, stderr);
    }
}
