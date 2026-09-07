using System.Diagnostics;

namespace AudioBridge.Windows;

/// <summary>
/// Opens the two UDP ports AudioBridge needs.
///
/// Windows Firewall silently drops inbound broadcasts by default, which looks to the user
/// exactly like the other PC not existing. Adding the rule needs elevation, so this runs
/// netsh via a UAC prompt rather than requiring the whole app to run as administrator.
/// </summary>
public static class FirewallRule
{
    public const string RuleName = "AudioBridge";

    public static bool Add(int audioPort, int discoveryPort, out string message)
    {
        var script =
            $"netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1 & " +
            $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
            $"protocol=UDP localport={audioPort},{discoveryPort}";

        try
        {
            var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script)
            {
                UseShellExecute = true,
                Verb = "runas", // triggers the UAC prompt
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            process?.WaitForExit(15000);
            var succeeded = process is { ExitCode: 0 };
            message = succeeded
                ? $"Windows Firewall now allows UDP ports {audioPort} and {discoveryPort} in."
                : "The firewall rule was not added.";
            return succeeded;
        }
        catch (Exception ex)
        {
            // Almost always the user declining the UAC prompt.
            message = "Firewall change cancelled: " + ex.Message;
            return false;
        }
    }
}
