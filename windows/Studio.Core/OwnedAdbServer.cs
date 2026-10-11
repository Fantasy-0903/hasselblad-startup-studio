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
    public OwnedAdbServer(string executable, int timeout = 10000)
    {
        using (var reservation = new TcpListener(IPAddress.Loopback, 0)) { reservation.Start(); Port = ((IPEndPoint)reservation.LocalEndpoint).Port; }
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        info.ArgumentList.Add("-L"); info.ArgumentList.Add("tcp:127.0.0.1:" + Port); info.ArgumentList.Add("nodaemon"); info.ArgumentList.Add("server");
        ClearOverrides(info);
        task = new Process { StartInfo = info };
        try
        {
            Check.That(task.Start(), "无法启动内置文件传输服务。"); ProcessId = task.Id; task.StandardInput.Close();
            // Drain private child's pipes; never store server device identifiers in UI or diagnostics.
            task.OutputDataReceived += (_, _) => { }; task.ErrorDataReceived += (_, _) => { }; task.BeginOutputReadLine(); task.BeginErrorReadLine();
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeout)
            {
                Check.That(!task.HasExited, "内置文件传输服务未启动，请完整解压安装包。");
                using var client = new TcpClient();
                try
                {
                    if (client.ConnectAsync(IPAddress.Loopback, Port).Wait(150) && client.Connected) { Check.That(!task.HasExited, "内置文件传输服务已退出。"); return; }
                }
                catch (AggregateException) { }
                catch (SocketException) { }
                Thread.Sleep(50);
            }
            throw new StudioException("内置文件传输服务启动超时，请重新检测。");
        }
        catch { Dispose(); throw; }
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
