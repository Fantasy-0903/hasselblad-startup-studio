using System.Runtime.InteropServices;
namespace StartupStudio;

public static class WindowsAdbInterface
{
    private static readonly Guid ClassId = new("F72FE0D4-CBCB-407D-8814-9ED673D0DD6B");
    private static readonly Guid FactoryClassId = new("CC135687-5267-4313-B0C7-C344609D6EF0");
    private static readonly Guid DeviceClassId = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceData { public int Size; public Guid Class; public int Flags; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enumerate_Classes(uint index, out Guid classGuid, uint flags);

    public static bool Matches(string path, ushort productId) =>
        productId is 9 or 10 && path.Contains("#vid_2756&pid_" + productId.ToString("x4") + "&mi_06#", StringComparison.OrdinalIgnoreCase);

    // Inspect only Windows interface registration. Do not open an endpoint or
    // change the driver's configuration while waiting for gadget enumeration.
    public static bool Present(ushort productId) => Paths(ClassId).Any(path => Matches(path, productId));
    public static bool MatchesFactory(string path, ushort productId) =>
        productId is 9 or 10 && path.Contains("#vid_2756&pid_" + productId.ToString("x4") + "&mi_03#", StringComparison.OrdinalIgnoreCase);
    public static bool SupportedDevicePath(string path) => new[] { "#vid_2756&pid_0009#", "#vid_2756&pid_000a#" }.Any(value => path.Contains(value, StringComparison.OrdinalIgnoreCase));
    public static int SupportedDeviceCount => Paths(DeviceClassId).Count(SupportedDevicePath);
    internal static (string Path, ushort ProductId)[] FactoryPaths()
    {
        // The observed factory GUID is fast, but a valid WinUSB installation can
        // register another interface GUID. Search registered interface classes only
        // when that path is absent; WinUsbFactory still verifies MI_03 and endpoints.
        var preferred = SelectFactoryPaths(Paths(FactoryClassId));
        if (preferred.Length != 0) return preferred;
        if (SupportedDeviceCount == 0) return [];
        var found = new List<(string Path, ushort ProductId)>();
        foreach (var guid in RegisteredInterfaceClasses())
        {
            if (guid == FactoryClassId || guid == ClassId || guid == DeviceClassId) continue;
            found.AddRange(SelectFactoryPaths(Paths(guid)));
        }
        return found.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static (string Path, ushort ProductId)[] SelectFactoryPaths(IEnumerable<string> paths) => paths
        .Select(path => (Path: path, ProductId: (ushort)(MatchesFactory(path, 9) ? 9 : MatchesFactory(path, 10) ? 10 : 0)))
        .Where(item => item.ProductId != 0).ToArray();
    public static IEnumerable<Guid> RegisteredInterfaceClasses()
    {
        Check.That(OperatingSystem.IsWindows(), "USB 接口枚举仅支持 Windows。");
        for (uint index = 0; index < 65536; index++)
        {
            var result = CM_Enumerate_Classes(index, out var guid, 1);
            if (result == 0x25) yield break; // CR_NO_SUCH_VALUE
            if (result == 0x1f) continue; // CR_INVALID_DATA: an empty class entry
            Check.That(result == 0, "Windows USB 接口类枚举失败。");
            yield return guid;
        }
        throw new StudioException("Windows USB 接口类列表异常。");
    }
    private static string[] Paths(Guid guid)
    {
        Check.That(OperatingSystem.IsWindows(), "USB 接口枚举仅支持 Windows。");
        var paths = new List<string>(); var set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        Check.That(set != new IntPtr(-1), "无法枚举 Windows ADB 接口。");
        try
        {
            for (uint i = 0; i < 1024; i++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data))
                {
                    Check.That(Marshal.GetLastWin32Error() == 259, "Windows USB 接口枚举失败。"); return paths.ToArray();
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var length, IntPtr.Zero);
                // A registered interface can disappear between the two calls.
                if (length < 8) continue;
                Check.That(length <= 65536, "Windows ADB 接口路径无效。");
                var buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, length, out _, IntPtr.Zero)) paths.Add(Marshal.PtrToStringUni(buffer + 4) ?? "");
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new StudioException("Windows ADB 接口列表异常。");
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
}
