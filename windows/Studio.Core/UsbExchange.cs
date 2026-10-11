using System.Diagnostics;
namespace StartupStudio;

public readonly record struct BulkResult(int Code, int Transferred);
public interface IUsbBulk { BulkResult Transfer(byte endpoint, byte[] bytes, uint timeout); }
public static class UsbExchange
{
    public static string Send(IUsbBulk bulk, string command, uint cookie, uint timeout, Action<string> trace, Func<long>? clock = null)
    {
        Check.That(timeout > 0, "USB 等待时间无效。");
        var watch = Stopwatch.StartNew(); clock ??= () => watch.ElapsedMilliseconds;
        var request = UsbProtocol.Request(command, cookie); var packet = new byte[1024]; var started = clock();
        while (clock() - started < 50)
        {
            var stale = bulk.Transfer(0x85, packet, 5); Check.That(stale.Transferred is >= 0 and <= 1024, "USB 回包长度异常。");
            if (stale.Code == -7) break; NativeUsb.Require(stale.Code, "旧回包清理");
        }
        var sent = bulk.Transfer(4, request, 2000); NativeUsb.Require(sent.Code, "请求发送"); Check.That(sent.Transferred == 257, "USB 请求发送不完整。");
        var parser = new ReplyParser(cookie); started = clock();
        while (clock() - started < timeout)
        {
            var remaining = timeout - (clock() - started); if (remaining <= 0) break;
            var reply = bulk.Transfer(0x85, packet, (uint)Math.Min(remaining, 200));
            if (reply.Code != 0 && reply.Code != -7) NativeUsb.Require(reply.Code, "回包读取");
            Check.That(reply.Transferred is >= 0 and <= 1024, "USB 回包长度异常。");
            // A timeout may still carry a complete final frame. Parse its bytes first.
            if (reply.Transferred > 0) parser.Feed(packet.AsSpan(0, reply.Transferred));
            if (parser.Complete) { trace($"USB 请求完成；CRC异常{parser.BadCrc}，其他请求{parser.Foreign}"); return parser.Result; }
        }
        throw new StudioException("LIBUSB_ERROR_TIMEOUT：相机未返回最终确认，请保持开机后重新检测。");
    }
}
