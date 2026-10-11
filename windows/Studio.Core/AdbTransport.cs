using System.Diagnostics;
using System.Text;
namespace StartupStudio;

public sealed class AdbProcess(string executable)
{
    public string? Serial { get; set; }
    public bool UsbOnly { get; set; }
    public int? ServerPort { get; set; }
    private static async Task<byte[]> Bounded(Stream stream, int limit)
    {
        using var output = new MemoryStream(); var block = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(block)) > 0) { Check.That(output.Length + read <= limit, "ADB 回包超过允许长度。"); output.Write(block, 0, read); }
        return output.ToArray();
    }
    public byte[] Run(string[] arguments, int timeout = 30000, bool selected = true)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        OwnedAdbServer.ClearOverrides(start);
        if (ServerPort != null) { start.ArgumentList.Add("-H"); start.ArgumentList.Add("127.0.0.1"); start.ArgumentList.Add("-P"); start.ArgumentList.Add(ServerPort.Value.ToString()); }
        if (selected && UsbOnly) start.ArgumentList.Add("-d");
        else if (selected && Serial != null) { start.ArgumentList.Add("-s"); start.ArgumentList.Add(Serial); }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var task = new Process { StartInfo = start };
        try { Check.That(task.Start(), "无法启动内置 ADB。"); } catch (System.ComponentModel.Win32Exception) { throw new StudioException("无法启动内置 ADB，请完整解压安装包。"); }
        task.StandardInput.Close(); var output = Bounded(task.StandardOutput.BaseStream, 4 * 1024 * 1024); var errors = Bounded(task.StandardError.BaseStream, 256 * 1024);
        if (!task.WaitForExit(timeout)) { task.Kill(); task.WaitForExit(3000); throw new StudioException("ADB 操作超时，请保持相机开机并检查数据线。"); }
        var bytes = output.GetAwaiter().GetResult(); var error = Encoding.UTF8.GetString(errors.GetAwaiter().GetResult()).ToLowerInvariant();
        if (task.ExitCode != 0)
        {
            if (task.ExitCode < 0) throw new StudioException(AdbStartupFailure.Message(task.ExitCode, error));
            if (error.Contains("permission denied")) throw new StudioException("文件传输被相机权限拒绝，安装未继续。");
            if (error.Contains("more than one device") || error.Contains("more than one emulator")) throw new StudioException("检测到多台 USB 设备，请只保留这台相机后重试。");
            if (error.Contains("no devices") || error.Contains("device not found") || error.Contains("device offline")) throw new StudioException("文件传输时相机连接已断开，请唤醒后重新检测。");
            throw new StudioException("ADB 操作失败（退出码 " + task.ExitCode + "），请重新检测相机。");
        }
        return bytes;
    }
}
public sealed class ManagedCameraLink : IManagedCameraLink
{
    private UsbLink? usb; private AdbProcess? adb; private string? originalUsb, fingerprint, model;
    private OwnedAdbServer? server;
    private bool switched;
    private readonly string executable;
    private readonly Action<string> log, trace;
    public ushort? ProductId => usb?.ProductId;
    public ManagedCameraLink(string resources, Action<string> log, Action<string> trace)
    {
        executable = Path.Combine(resources, "bin", "adb.exe"); this.log = log; this.trace = trace; usb = new(trace);
    }
    public string Command(string text, uint timeout = 12000) => (usb ?? throw new StudioException("USB 会话已断开，请重新检测相机。")).Command(text, timeout);
    private static string Identify(ICameraLink link) => Models.Detect(link.Command("getprop ro.product.device"), link.Command("getprop hbl.usb_pid"), link.Command("getprop hbl.usb_prod_name"), link.ProductId);
    private static string Fingerprint(ICameraLink link) => link.Command("sha256sum /factory_data/settings.ini").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    private void CloseUsb() { usb?.Dispose(); usb = null; }
    private AdbProcess Prepare()
    {
        if (adb != null) { Check.That(server?.Running == true, "本应用文件传输服务已退出，请重新检测。"); return adb; }
        Check.That(File.Exists(executable), "内置 ADB 缺失，安装尚未开始，请完整解压安装包。");
        fingerprint = Fingerprint(this); model = Identify(this); Check.That(Check.IsHash(fingerprint), "无法确认相机身份。");
        var config = Command("getprop sys.usb.config");
        Check.That(config is "rndis,mass_storage,bulk,acm" or "rndis,mass_storage,bulk,acm,adb", "当前 USB 模式不受支持，安装尚未开始。"); originalUsb = config;
        log("正在准备文件传输通道");
        foreach (var name in new[] { "AdbWinApi.dll", "AdbWinUsbApi.dll" }) Check.That(File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, name)), "内置文件传输组件缺失：" + name + "。安装尚未开始，请完整解压安装包。");
        var version = new AdbProcess(executable).Run(["version"], 10000, false);
        Check.That(Encoding.UTF8.GetString(version).Contains("Android Debug Bridge version"), "内置 ADB 版本自检未通过，安装尚未开始。");
        server = new OwnedAdbServer(executable, diagnostic: trace);
        var process = new AdbProcess(executable) { UsbOnly = true, ServerPort = server.Port };
        if (!config.Split(',').Contains("adb"))
        {
            switched = true;
            try { Command("/system/bin/toybox nohup /system/bin/sh -c 'sleep 1;setprop sys.usb.config none;sleep 1;setprop sys.usb.config rndis,mass_storage,bulk,acm,adb' >/dev/null 2>&1 &", 5000); }
            catch (StudioException e) when (e.Message.Contains("LIBUSB_ERROR_NO_DEVICE") || e.Message.Contains("LIBUSB_ERROR_TIMEOUT")) { /* Delayed gadget switch can disconnect after acceptance. */ }
        }
        CloseUsb(); var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 40000)
        {
            Check.That(server.Running, "本应用文件传输服务已退出，请重新检测。");
            // The Windows native backend registers USB transports without a devpath,
            // so devices -l need not contain usb:. Let ADB itself enforce USB-only selection.
            string? candidate = null;
            try { candidate = TransportRules.UsbSerial(Encoding.UTF8.GetString(process.Run(["get-serialno"], 5000))); }
            catch (StudioException e) when (e.Message.Contains("连接已断开")) { /* Wait for USB re-enumeration. */ }
            if (candidate != null)
            {
                process.Serial = candidate;
                var adbPlatform = Encoding.UTF8.GetString(process.Run(["shell", "getprop ro.product.device"], 5000)).Trim();
                Check.That(adbPlatform == "eagle2_ec1706_native", "物理 USB 文件传输设备不是支持的相机。");
                UsbLink fresh;
                try { fresh = new(trace); } catch (StudioException) { Thread.Sleep(250); continue; }
                try
                {
                    Check.That(Identify(fresh) == model && Fingerprint(fresh) == fingerprint, "文件传输通道对应的相机发生变化，已停止操作。");
                    Check.That(fresh.Command("getprop sys.usb.state").Split(',').Contains("adb"), "文件传输通道尚未生效。");
                }
                catch { fresh.Dispose(); throw; }
                usb = fresh; adb = process; log("文件传输通道已就绪"); return process;
            }
            Thread.Sleep(250);
        }
        throw new StudioException("等待文件传输通道超时，请检查 Windows ADB 接口驱动后重新检测。");
    }
    private static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "startup-transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); return dir;
    }
    public byte[] ReadFile(string path, int size)
    {
        TransportRules.ValidPath(path); Check.That(size is >= 0 and <= 4 * 1024 * 1024, "文件大小无效。");
        var process = Prepare(); var dir = TempFolder();
        try
        {
            Check.That(TransportRules.UsbSerial(Encoding.UTF8.GetString(process.Run(["get-serialno"], 5000))) == process.Serial, "物理 USB 文件传输设备发生变化。");
            var file = Path.Combine(dir, "data"); process.Run(["pull", path, file]); var data = File.ReadAllBytes(file);
            Check.That(data.Length == size && Command("sha256sum " + path).Split(' ')[0] == Check.Hash(data), "相机文件读取或校验失败，未继续安装。");
            log(path == CameraPaths.Library ? "原厂备份读取完成，校验通过" : "图片读回校验通过"); return data;
        }
        finally { Directory.Delete(dir, true); }
    }
    public void WriteFile(byte[] data, string path)
    {
        TransportRules.ValidPath(path); var process = Prepare(); var dir = TempFolder();
        try
        {
            Check.That(TransportRules.UsbSerial(Encoding.UTF8.GetString(process.Run(["get-serialno"], 5000))) == process.Serial, "物理 USB 文件传输设备发生变化。");
            var file = Path.Combine(dir, "data"); File.WriteAllBytes(file, data); process.Run(["push", file, path]);
            Check.That(Command("sha256sum " + path).Split(' ')[0] == Check.Hash(data), "相机文件上传校验失败。");
            trace("ADB 文件上传完成；长度与 SHA-256 核对通过");
        }
        finally { Directory.Delete(dir, true); }
    }
    public void Finish()
    {
        if (!switched || originalUsb == null) { adb = null; CloseUsb(); StopServer(); return; }
        log("正在恢复相机 USB 连接状态");
        var restore = "/system/bin/toybox nohup /system/bin/sh -c 'sleep 1;setprop sys.usb.config none;sleep 1;setprop sys.usb.config " + originalUsb + "' >/dev/null 2>&1 &";
        var sent = false;
        if (usb != null) { try { Check.That(Fingerprint(usb) == fingerprint && Identify(usb) == model, "相机身份变化，禁止修改另一台相机的 USB 状态。"); usb.Command(restore, 5000); sent = true; } catch (StudioException) { /* Reopen and check identity before retry. */ } }
        adb = null; CloseUsb(); StopServer(); var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 25000)
        {
            UsbLink fresh; try { fresh = new(trace); } catch (StudioException) { Thread.Sleep(250); continue; }
            using (fresh)
            {
                Check.That(Fingerprint(fresh) == fingerprint && Identify(fresh) == model, "相机身份变化，已停止 USB 状态恢复。");
                var config = fresh.Command("getprop sys.usb.config"); var state = fresh.Command("getprop sys.usb.state");
                if (config == originalUsb && state == originalUsb) { switched = false; log("USB 连接状态已恢复"); return; }
                if (!sent && config == "rndis,mass_storage,bulk,acm,adb") { try { fresh.Command(restore, 5000); sent = true; } catch (StudioException e) when (e.Message.Contains("LIBUSB_ERROR_NO_DEVICE") || e.Message.Contains("LIBUSB_ERROR_TIMEOUT")) { sent = true; } }
            }
            Thread.Sleep(250);
        }
        throw new StudioException("USB 原状态恢复未确认；请完整关机再开机后重新检测。");
    }
    private void StopServer() { var own = server; server = null; own?.Dispose(); }
    public void Dispose() { CloseUsb(); StopServer(); }
}
