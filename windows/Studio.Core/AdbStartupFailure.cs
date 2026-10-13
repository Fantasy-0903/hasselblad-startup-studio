namespace StartupStudio;

public static class AdbStartupFailure
{
    public static string Category(int exitCode, string error)
    {
        var code = unchecked((uint)exitCode); var text = error.ToLowerInvariant();
        if (code is 0xc0000135 or 0xc000007b or 0xc0000142) return "runtime";
        if (code is 0xc0000428 or 0xc0000022) return "blocked";
        if (text.Contains("listening on specified hostname currently unsupported")) return "listener";
        if (text.Contains("cannot bind") || text.Contains("could not listen")) return "port";
        if (text.Contains("permission denied") || text.Contains("access is denied")) return "permission";
        return "unknown";
    }
    public static string Message(int exitCode, string error)
    {
        var reason = Category(exitCode, error) switch
        {
            "runtime" => "内置 ADB 的运行依赖无法加载，请核对完整解压后的配套 DLL。",
            "blocked" => "系统阻止了内置 ADB 启动，请核对系统或安全软件的拦截记录。",
            "listener" => "内置 ADB 不支持该监听地址，请更新 Windows 测试版。",
            "port" => "本地文件传输端口无法监听，请重新连接后重试。",
            "permission" => "内置 ADB 的本地权限被拒绝，请核对权限或拦截记录。",
            _ => "内置文件传输服务启动失败，请保留诊断日志反馈。"
        };
        return reason + "（退出码 0x" + unchecked((uint)exitCode).ToString("X8") + "；安装尚未开始）";
    }
}
