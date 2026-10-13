using System.Buffers.Binary;
using System.Text;
namespace StartupStudio;

public static class UsbProtocol
{
    public static ushort Crc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data) { crc ^= (ushort)(b << 8); for (var i = 0; i < 8; i++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1); }
        return crc;
    }
    public static byte[] Request(string command, uint cookie)
    {
        var bytes = Encoding.UTF8.GetBytes(command); Check.That(bytes.Length <= 231 && cookie != 0 && !command.Contains('\0'), "内部 USB 命令长度或标识无效。");
        var r = new byte[257]; new byte[] { 10, 0, 8, 5, 252 }.CopyTo(r, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(5), 65); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(13), cookie);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(21), 10); bytes.CopyTo(r, 25);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(17), Crc(r.AsSpan(21, 236))); return r;
    }
}
public sealed class ReplyParser(uint cookie)
{
    private readonly List<byte> buffer = [];
    private readonly MemoryStream output = new();
    public bool Complete { get; private set; }
    public int BadCrc { get; private set; }
    public int Foreign { get; private set; }
    public string Result { get { Check.That(Complete, "未取得最终回复。"); return new UTF8Encoding(false, true).GetString(output.ToArray()).Trim(); } }
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        Check.That(!Complete && buffer.Count + bytes.Length <= 8192, "USB 回包缓冲区异常。");
        foreach (var b in bytes) buffer.Add(b);
        while (buffer.Count >= 5)
        {
            var start = -1;
            for (var i = 0; i + 5 <= buffer.Count; i++) if (buffer[i] == 9 && buffer[i + 1] == 0 && buffer[i + 2] == 5 && buffer[i + 3] == 8 && buffer[i + 4] == 252) { start = i; break; }
            if (start < 0) { if (buffer.Count > 4) buffer.RemoveRange(0, buffer.Count - 4); break; }
            if (start > 0) buffer.RemoveRange(0, start); if (buffer.Count < 257) break;
            var frame = buffer.Take(257).ToArray(); uint Word(int at) => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(at));
            if (Word(17) != UsbProtocol.Crc(frame.AsSpan(21, 236))) { BadCrc++; buffer.RemoveAt(0); continue; }
            if (Word(5) == 65 && Word(13) == cookie)
            {
                if (Word(9) == 4)
                {
                    var data = frame.AsSpan(25, 232); var end = data.IndexOf((byte)0); if (end >= 0) data = data[..end];
                    Check.That(output.Length + data.Length <= 4 * 1024 * 1024, "USB 输出超过允许长度。"); output.Write(data);
                }
                else { Check.That(Word(21) == 0, "相机命令执行失败（状态 " + Word(21) + "）。"); Complete = true; }
            }
            else Foreign++;
            buffer.RemoveRange(0, 257); if (Complete) break;
        }
    }
}
public static class TransportRules
{
    public static bool SafeReadRetry(string command) => Check.Match(command, "getprop [a-zA-Z0-9_.-]+|sha256sum /[A-Za-z0-9/_.-]+|ls -Z /[A-Za-z0-9/_.-]+|stat -c (%s|'%a %u %g %s %Y') /[A-Za-z0-9/_.-]+") || command == "prodconfig-tool --print 2>&1 | sed -n '/^SuVariant:/p'";
    public static string[] PhysicalCameras(string text) => text.Split('\n').Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Where(f => f.Length >= 2 && f[1] == "device" && f.Contains("device:eagle2_ec1706_native") && f.Any(x => x.StartsWith("usb:")) && Check.Match(f[0], "[A-Za-z0-9._-]+")).Select(f => f[0]).ToArray();
    public static void ValidPath(string path) => Check.That(Check.Match(path, "/[A-Za-z0-9/_.-]+"), "文件传输路径无效。");
    public static string UsbSerial(string output)
    {
        var serial = output.Trim(); Check.That(serial != "unknown" && Check.Match(serial, "[A-Za-z0-9._-]+"), "无法确认唯一的物理 USB 相机。"); return serial;
    }
}
