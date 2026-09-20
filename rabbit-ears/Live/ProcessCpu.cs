using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace RabbitEars.Live;

/// <summary>CPU use of child processes via `ps` (percent of one core, summed). 0 where `ps` is not available.</summary>
public static class ProcessCpu
{
    public static double Percent(IReadOnlyCollection<int> pids)
    {
        if (pids.Count == 0 || OperatingSystem.IsWindows()) return 0;
        try
        {
            var info = new ProcessStartInfo("ps") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string argument in new[] { "-o", "%cpu=", "-p", string.Join(',', pids) }) info.ArgumentList.Add(argument);
            using Process? ps = Process.Start(info);
            if (ps is null) return 0;
            string output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(2000);
            return output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Sum(text => double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            return 0;
        }
    }
}
