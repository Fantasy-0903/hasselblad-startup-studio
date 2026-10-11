# 依赖、许可与来源

本项目原创 Swift、C、汇编、Python 源码及文档使用 [MIT License](LICENSE)。
本许可不授予相机厂商固件、商标、用户图片或另行提供的第三方组件的权利。

| 组件 | 用途与提供方式 |
| --- | --- |
| macOS SDK / Swift / clang | 系统框架与编译工具；由开发者自行安装，不随源码包分发 |
| libusb | App 运行时动态库；由构建者提供，两种 Mac 架构均需包含；保留输入发行版的 COPYING，构建时复制为 `libusb-COPYING.txt` |
| Android platform-tools / adb | 大文件 USB 传输；构建者自行准备可用的 Universal 可执行文件与对应许可文件，许可复制为 `Android-platform-tools.txt`；不随本源码仓库分发 |
| pyelftools | 本地读取 ELF，依赖版本见 `requirements-dev.txt`；不进入 App |
| Unicorn | 本地执行实际 ARM64 指令检查；不进入 App |

源码包不含依赖的二进制发行版或厂商固件。重新分发 App 时应保留本项目及实际所用组件的通知，
并根据所选组件发行版附带其要求的材料；仅包含一份本项目 MIT 文件不能替代组件许可。

USB 工厂通道来自既有研究：VID `2756`、PID `0009/000A`、接口 3、257 字节帧、command 65、CRC-XMODEM。
本项目 C 桥接实现和固定合成请求样本曾与同一作者的另一桌面工具纯协议构建函数逐字节比对；
源码包不复制该工具的相机功能载荷、产品入口或依赖发行包，正常构建和测试不需要它。

原厂 `eagle-backend.so` 仅作为构建者自行准备的本地输入，必须匹配固定 SHA-256。
适配差异、本地模拟与运行时复用原厂绘制循环，均不改变厂商原始资源的权属；原厂 H、大地与周年图片不随源码分发。
