# Windows 工厂接口与 ADB 共存

## 问题及实现

用户在 Windows 连接检查成功后，安装停在首次原厂备份读取前：先出现文件传输准备超时，再出现 USB 原状态恢复未确认。旧界面优先显示收尾失败，掩盖了首次失败。

在包内 libusb 1.0.29 与 ADB 37.0.1 上做接口打开诊断，未启动 ADB 时设备打开及接口 3 claim 返回 0；启动私有 ADB 后，设备打开返回 -3。libusb 的 Windows `winusbx_open` 会为组合设备的多个 WinUSB 接口打开句柄，其中 MI_06 返回 Windows 错误 5（访问拒绝）。只调整等待时间或重建上下文不能解决接口占用冲突；失败的组合打开也可能保留部分已打开接口的句柄。

`WinUsbFactory` 通过 SetupAPI 查找已登记的 Hasselblad 工厂接口类，匹配 VID 2756、PID 0009/000A、MI_03，再直接调用系统 WinUSB API。已实测的接口 GUID 优先；找不到时枚举 Windows 登记的设备接口类，按同样的 VID/PID 与 MI_03 匹配，并继续核对唯一受支持物理相机、接口 3、当前 alternate 0，以及 bulk OUT 0x04 / IN 0x85。仅改变主机侧超时策略。USB 请求、CRC/cookie、回包解析和身份校验沿用原实现。ADB 已暴露但目标工厂接口尚未登记时，有限等待重试，不尝试会打开其他接口的组合 libusb 路径。

当前直接路径支持已登记的 WinUSB 工厂接口；不自动安装驱动，其他驱动配置的真实相机兼容性仍待验收。Mac 代码、安装/恢复脚本、固定固件门禁与首次备份保护未改变。

## 已完成的验证

- Windows 11 x64、.NET 10：148 项合成数据、进程替身及控件离线检查通过。新增 WinUSB ABI、错误映射、单物理相机门禁、恢复确认与界面错误保留检查。
- 原生 Windows EXE 的 `--smoke-ui` 窗口启动通过；该入口禁止建立 USB 会话。
- 2026-10-12，一台第一代 X2D 100C：生产 `WinUsbFactory` 在 ADB 启动前、运行中、重复打开及退出后均能打开工厂接口并验证端点。
- 同一相机上，使用生产 USB 请求及回包代码读取 `ro.product.device`、`sys.usb.config`、`sys.usb.state`；ADB 并发运行时成功，诊断前后属性相同。没有安装画面、写相机文件或发送 USB 配置变更命令。
- 2026-10-13 修订在 macOS 上交叉编译无警告，150 项合成数据、进程替身与控件离线检查通过；其中新增工厂接口使用其他 GUID 的路径选择、接口登记延迟后的 USB 恢复检查。Windows 云端检查将另行运行；其他 GUID 的真实驱动尚未实机验收。

上述为只读实机证据，不代表完整安装/连续换图/恢复、断电断线或其他机型的验收。原始日志、相机身份、电脑路径、固件、用户图片和备份不随 PR 提交。

## 复现离线检查

```sh
dotnet build windows/Studio.Tests/Studio.Tests.csproj -c Release --artifacts-path outputs/windows-build
dotnet outputs/windows-build/bin/Studio.Tests/release/Studio.Tests.dll .
dotnet publish windows/Studio.App/Studio.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None --artifacts-path outputs/windows-build -o outputs/windows-ci-app
```

在 Windows 上运行发布 EXE 的 `--smoke-ui`，核对 `smoke-results/result.json`。真实 ADB 离线入口要求先断开所有受支持相机；不依赖 `--one-device` 作为硬件隔离。

待设备所有者后续验收：先只读检查并保留首次备份，再安装测试图片、连续换图、恢复原厂，并确认 USB 状态及原厂属性。CI 和默认测试不执行这些操作。

参考：[libusb 1.0.29 Windows 后端](https://github.com/libusb/libusb/blob/v1.0.29/libusb/os/windows_winusb.c)、[Microsoft WinUSB 使用说明](https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/using-winusb-api-to-communicate-with-a-usb-device)。
