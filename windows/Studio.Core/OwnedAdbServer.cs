using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
namespace StartupStudio;

// Own the exact foreground child process, not a user's pre-existing global ADB server.
public sealed class OwnedAdbServer : IDisposable
{
    private readonly Process task;
    public int Port { get; }
    public int ProcessId { get; }
    public bool Running => !task.HasExited;
    // Keep the original compiled application's two-argument constructor compatible.
    public OwnedAdbServer(string executable, int timeout) : this(executable, timeout, null, null) { }
    public OwnedAdbServer(string executable, int timeout = 10000, Action<string>? diagnostic = null, string? deviceFilter = null)
    {
        using (var reservation = new TcpListener(IPAddress.Loopback, 0)) { reservation.Start(); Port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        // ADB accepts localhost for listening; a numeric host is rejected by its listener.
        info.ArgumentList.Add("-L"); info.ArgumentList.Add("tcp:localhost:" + Port); info.ArgumentList.Add("nodaemon"); info.ArgumentList.Add("server");
        if (deviceFilter != null) { info.ArgumentList.Add("--one-device"); info.ArgumentList.Add(deviceFilter); }
        ClearOverrides(info);
        info.Environment["ADB_MDNS_AUTO_CONNECT"] = "0";
        task = new Process { StartInfo = info };
        try
        {
            Check.That(task.Start(), "无法启动内置文件传输服务。"); ProcessId = task.Id; task.StandardInput.Close();
            // Bound retained output, continue draining pipes, and report only a classified error.
            var stdout = Sample(task.StandardOutput); var stderr = Sample(task.StandardError);
            void CheckRunning()
            {
                if (!task.HasExited) return;
                var error = stderr.GetAwaiter().GetResult(); _ = stdout.GetAwaiter().GetResult();
                diagnostic?.Invoke("ADB startup exit=0x" + unchecked((uint)task.ExitCode).ToString("X8") + " category=" + AdbStartupFailure.Category(task.ExitCode, error));
                throw new StudioException(AdbStartupFailure.Message(task.ExitCode, error));
            }
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeout)
            {
                CheckRunning();
                using var client = new TcpClient();
                try
                {
                    if (client.ConnectAsync(IPAddress.Loopback, Port).Wait(150) && client.Connected)
                    {
                        using var stream = client.GetStream(); stream.ReadTimeout = 500; stream.WriteTimeout = 500;
                        stream.Write(System.Text.Encoding.ASCII.GetBytes("000chost:version")); var reply = new byte[12]; stream.ReadExactly(reply);
                        if (Check.Match(System.Text.Encoding.ASCII.GetString(reply), "OKAY0004[0-9a-fA-F]{4}")) { CheckRunning(); return; }
                    }
                }
                catch (AggregateException) { }
                catch (SocketException) { }
                catch (IOException) { }
                Thread.Sleep(50);
            }
            throw new StudioException("内置文件传输服务启动超时，请重新检测。");
        }
        catch { Dispose(); throw; }
    }
    private static async Task<string> Sample(TextReader reader)
    {
        var result = new System.Text.StringBuilder(); var block = new char[4096]; int read;
        while ((read = await reader.ReadAsync(block)) > 0) { var keep = Math.Min(read, 8192 - result.Length); if (keep > 0) result.Append(block, 0, keep); }
        return result.ToString();
    }
    internal static void ClearOverrides(ProcessStartInfo info)
    {
        foreach (var name in new[] { "ADB_SERVER_SOCKET", "ANDROID_ADB_SERVER_PORT", "ANDROID_SERIAL", "ADB_TRACE" }) info.Environment.Remove(name);
    }
    public void Dispose()
    {
        try { if (!task.HasExited) { task.Kill(); Check.That(task.WaitForExit(5000), "本应用文件传输进程尚未退出，请关闭应用后重试。"); } }
        catch (InvalidOperationException) { /* Process never started, or was already reaped. */ }
        finally { task.Dispose(); }
    }
}
