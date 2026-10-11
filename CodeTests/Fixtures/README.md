# USB 请求固定样本

`usb-shell-request.json` 是合成的只读请求：命令 `getprop ro.product.device`，cookie 固定为 `0x12345678`。
不包含相机响应、真实设备身份或原厂固件字节。257 字节请求曾与另一桌面工具的纯协议构建函数逐字节比对。
样本固定保存，测试不会调用被测 C 请求生成器来生成期望值，也不导入或执行另一工具的设备入口。

`check_usb_transport.py` 默认只使用本样本；显式提供 `--reference` 才额外抽取并比对外部脚本的两个纯函数。
