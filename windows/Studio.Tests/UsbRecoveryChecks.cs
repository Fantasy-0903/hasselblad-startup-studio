using StartupStudio;
using System.Runtime.InteropServices;

static class UsbRecoveryChecks
{
    static void Require(bool value) { if (!value) throw new Exception("USB recovery assertion failed"); }
    sealed class Session(Func<string, string> command, Action disposed) : IUsbCameraLink
    {
        public ushort? ProductId => 9;
        public string Command(string text, uint timeout = 12000) => command(text);
        public void Dispose() => disposed();
    }
    public static void Run(Action<string, Action> test)
    {
        test("winusb-single-camera-gate-counts-physical-parents", () =>
        {
            Require(WindowsAdbInterface.SupportedDevicePath(@"\\?\usb#vid_2756&pid_0009#camera#{guid}"));
            Require(WindowsAdbInterface.SupportedDevicePath(@"\\?\usb#vid_2756&pid_000a#camera#{guid}"));
            Require(!WindowsAdbInterface.SupportedDevicePath(@"\\?\usb#vid_2756&pid_0009&mi_03#camera#{guid}"));
            Require(!WindowsAdbInterface.SupportedDevicePath(@"\\?\usb#vid_1234&pid_0009#phone#{guid}"));
        });
        test("winusb-factory-interface-excludes-adb-and-other-models", () =>
        {
            Require(WindowsAdbInterface.MatchesFactory(@"\\?\usb#vid_2756&pid_0009&mi_03#device#{guid}", 9));
            Require(!WindowsAdbInterface.MatchesFactory(@"\\?\usb#vid_2756&pid_0009&mi_06#device#{guid}", 9));
            Require(!WindowsAdbInterface.MatchesFactory(@"\\?\usb#vid_2756&pid_000a&mi_03#device#{guid}", 9));
        });
        test("winusb-native-descriptor-ABI", () =>
        {
            var descriptor = typeof(WinUsbFactory).GetNestedType("InterfaceDescriptor", System.Reflection.BindingFlags.NonPublic)!;
            var pipe = typeof(WinUsbFactory).GetNestedType("PipeInformation", System.Reflection.BindingFlags.NonPublic)!;
            Require(Marshal.SizeOf(descriptor) == 9 && Marshal.OffsetOf(descriptor, "Endpoints").ToInt32() == 4);
            Require(Marshal.SizeOf(pipe) == 12 && Marshal.OffsetOf(pipe, "Address").ToInt32() == 4 && Marshal.OffsetOf(pipe, "PacketSize").ToInt32() == 6);
        });
        test("winusb-errors-preserve-timeout-disconnect-and-access", () =>
        {
            Require(WinUsbFactory.ErrorCode(121) == -7 && WinUsbFactory.ErrorCode(1460) == -7);
            Require(WinUsbFactory.ErrorCode(1167) == -4 && WinUsbFactory.ErrorCode(5) == -3 && WinUsbFactory.ErrorCode(32) == -6);
        });
        test("windows-adb-interface-camera-only", () =>
        {
            Require(WindowsAdbInterface.Matches(@"\\?\usb#vid_2756&pid_0009&mi_06#device#{guid}", 9));
            Require(!WindowsAdbInterface.Matches(@"\\?\usb#vid_2756&pid_0009&mi_03#device#{guid}", 9));
            Require(!WindowsAdbInterface.Matches(@"\\?\usb#vid_1234&pid_0009&mi_06#device#{guid}", 9));
            Require(!WindowsAdbInterface.Matches(@"\\?\usb#vid_2756&pid_000a&mi_06#device#{guid}", 9));
        });
        test("usb-restore-reenumeration-and-property-timeout", () =>
        {
            long time = 0; var opened = 0; var disposed = 0; var writes = 0;
            IUsbCameraLink Open()
            {
                if (++opened == 1) throw new StudioException("LIBUSB_ERROR_NO_DEVICE");
                var number = opened;
                return new Session(command =>
                {
                    if (number == 2) throw new StudioException("LIBUSB_ERROR_TIMEOUT");
                    if (command.StartsWith("getprop")) return UsbStateRecovery.Normal;
                    writes++; return "";
                }, () => disposed++);
            }
            UsbStateRecovery.Restore(UsbStateRecovery.Normal, Open, _ => { }, _ => { }, () => time, ms => time += ms);
            Require(opened == 3 && disposed == 2 && writes == 0);
        });
        test("usb-restore-lost-ack-is-confirmed-after-reconnect", () =>
        {
            long time = 0; var writes = 0; var opened = 0; var disposed = 0;
            IUsbCameraLink Open() { opened++; return new Session(command =>
            {
                if (command.StartsWith("getprop")) return writes == 0 ? UsbStateRecovery.Transfer : UsbStateRecovery.Normal;
                writes++; throw new StudioException("LIBUSB_ERROR_NO_DEVICE");
            }, () => disposed++); }
            UsbStateRecovery.Restore(UsbStateRecovery.Normal, Open, _ => { }, _ => { }, () => time, ms => time += ms);
            Require(writes == 1 && opened == disposed);
        });
        test("usb-restore-unacknowledged-request-can-retry", () =>
        {
            long time = 0; var writes = 0; var verifies = 0;
            IUsbCameraLink Open() => new Session(command =>
            {
                if (command.StartsWith("getprop")) return writes >= 2 ? UsbStateRecovery.Normal : UsbStateRecovery.Transfer;
                writes++; throw new StudioException("LIBUSB_ERROR_TIMEOUT");
            }, () => { });
            UsbStateRecovery.Restore(UsbStateRecovery.Normal, Open, _ => verifies++, _ => { }, () => time, ms => time += ms);
            Require(writes == 2 && verifies > 2 && time >= 5000);
        });
        test("usb-restore-requires-config-and-state-confirmation", () =>
        {
            long time = 0; var writes = 0;
            IUsbCameraLink Open() => new Session(command =>
            {
                if (command == "getprop sys.usb.config") return UsbStateRecovery.Normal;
                if (command == "getprop sys.usb.state") return time >= 500 ? UsbStateRecovery.Normal : UsbStateRecovery.Transfer;
                writes++; return "";
            }, () => { });
            UsbStateRecovery.Restore(UsbStateRecovery.Normal, Open, _ => { }, _ => { }, () => time, ms => time += ms);
            Require(time >= 500 && writes == 0);
        });
        test("usb-restore-rejects-changed-camera-before-write", () =>
        {
            long time = 0; var writes = 0; var disposed = 0; var rejected = false;
            try
            {
                UsbStateRecovery.Restore(UsbStateRecovery.Normal, () => new Session(_ => { writes++; return ""; }, () => disposed++),
                    _ => throw new StudioException("相机身份变化，已停止 USB 状态恢复。"), _ => { }, () => time, ms => time += ms);
            }
            catch (StudioException e) when (e.Message.Contains("相机身份变化")) { rejected = true; }
            Require(rejected && writes == 0 && disposed == 1);
        });
        test("usb-restore-bounded-retry-does-not-report-success", () =>
        {
            long time = 0; var writes = 0; var rejected = false;
            try
            {
                UsbStateRecovery.Restore(UsbStateRecovery.Normal, () => new Session(command =>
                    { if (command.StartsWith("getprop")) return UsbStateRecovery.Transfer; writes++; return ""; }, () => { }),
                    _ => { }, _ => { }, () => time, ms => time += ms, timeout: 20000);
            }
            catch (StudioException e) when (e.Message.Contains("USB 原状态恢复未确认")) { rejected = true; }
            Require(rejected && writes == 3 && time == 20000);
        });
        test("usb-restore-does-not-overwrite-unknown-usb-mode", () =>
        {
            long time = 0; var writes = 0; var rejected = false;
            try
            {
                UsbStateRecovery.Restore(UsbStateRecovery.Normal, () => new Session(command =>
                    { if (command.StartsWith("getprop")) return "mtp,adb"; writes++; return ""; }, () => { }),
                    _ => { }, _ => { }, () => time, ms => time += ms, timeout: 1000);
            }
            catch (StudioException e) when (e.Message.Contains("USB 原状态恢复未确认")) { rejected = true; }
            Require(rejected && writes == 0);
        });
    }
}
