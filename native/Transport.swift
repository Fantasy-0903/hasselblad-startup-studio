import Foundation

final class DiagnosticFile {
    private let handle:FileHandle?
    init() {
        let dir=FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("X2DStartupStudio/Logs")
        try? FileManager.default.createDirectory(at:dir,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        let url=dir.appendingPathComponent("session-"+UUID().uuidString+".log")
        FileManager.default.createFile(atPath:url.path,contents:nil,attributes:[.posixPermissions:0o600])
        handle=try? FileHandle(forWritingTo:url)
    }
    func append(_ text:String) {try? handle?.write(contentsOf:Data((Date().description+" "+text+"\n").utf8))}
    deinit {try? handle?.close()}
}

protocol FileCameraLink: CameraLink {
    func readFile(_ path:String, size:Int) throws -> Data
    func writeFile(_ data:Data, to path:String) throws
}

// ADB identities are used only to select the physical transport, never displayed or saved.
func physicalCameras(_ text:String) -> [String] {
    text.split(separator:"\n").compactMap { line in
        let f=line.split(whereSeparator:{$0.isWhitespace}).map(String.init)
        guard f.count>=2,f[1]=="device",f.contains("device:eagle2_ec1706_native"),
              f.contains(where:{$0.hasPrefix("usb:")}),
              f[0].range(of:"^[A-Za-z0-9._-]+$",options:.regularExpression) != nil else {return nil}
        return f[0]
    }
}

final class ADBProcess {
    let executable:URL
    var serial:String?
    init(_ executable:URL) {self.executable=executable}
    func run(_ arguments:[String],timeout:TimeInterval=30,selected:Bool=true) throws -> Data {
        let dir=FileManager.default.temporaryDirectory.appendingPathComponent("startup-adb-"+UUID().uuidString)
        try FileManager.default.createDirectory(at:dir,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        defer {try? FileManager.default.removeItem(at:dir)}
        let output=dir.appendingPathComponent("output"),errors=dir.appendingPathComponent("errors")
        FileManager.default.createFile(atPath:output.path,contents:nil,attributes:[.posixPermissions:0o600])
        FileManager.default.createFile(atPath:errors.path,contents:nil,attributes:[.posixPermissions:0o600])
        let out=try FileHandle(forWritingTo:output),err=try FileHandle(forWritingTo:errors)
        defer {try? out.close();try? err.close()}
        let task=Process();task.executableURL=executable
        task.arguments=(selected ? serial.map {["-s",$0]} ?? []:[]) + arguments
        task.standardInput=FileHandle.nullDevice;task.standardOutput=out;task.standardError=err
        do {try task.run()} catch {throw StudioError("无法启动内置 ADB，请完整解压 App。")}
        let end=Date().addingTimeInterval(timeout)
        while task.isRunning && Date()<end {Thread.sleep(forTimeInterval:0.05)}
        if task.isRunning {
            task.terminate()
            // Only stop our client. Never kill an existing system ADB server.
            throw StudioError("ADB 操作超时，请保持相机开机并检查数据线。")
        }
        if task.terminationStatus != 0 {
            let raw=String(decoding:(try? Data(contentsOf:errors)) ?? Data(),as:UTF8.self).lowercased()
            if raw.contains("permission denied") {throw StudioError("文件传输被相机权限拒绝，安装未继续。")}
            if raw.contains("no devices") || raw.contains("device not found") || raw.contains("device offline") {throw StudioError("文件传输时相机连接已断开，请唤醒后重新检测。")}
            throw StudioError("ADB 操作失败（退出码 \(task.terminationStatus)），请重新检测相机。")
        }
        let data=try Data(contentsOf:output)
        try require(data.count<=4*1024*1024,"ADB 回包超过允许长度。")
        return data
    }
    func shell(_ command:String,timeout:TimeInterval=30) throws -> String {
        let data=try run(["shell",command],timeout:timeout)
        guard let text=String(data:data,encoding:.utf8) else {throw StudioError("ADB 文本响应无效。")}
        return text.trimmingCharacters(in:.whitespacesAndNewlines)
    }
}

// Control starts on HblM. File transfer uses the same physical camera via temporary ADB.
// No persist property, factory setting, serial number or display variant is written here.
final class ManagedCameraLink: FileCameraLink {
    private var usb:USBLink?
    private var adb:ADBProcess?
    private var originalUSB:String?
    private var switched=false
    private let executable:URL
    private let log:(String)->Void
    private let trace:(String)->Void
    var usbProductID:UInt16? {usb?.usbProductID}
    init(resources:URL,log:@escaping (String)->Void={_ in},trace:@escaping (String)->Void={_ in}) throws {
        executable=resources.appendingPathComponent("bin/adb");self.log=log;self.trace=trace
        usb=try USBLink(log:trace)
    }
    func command(_ text:String,timeout:UInt32) throws -> String {
        guard let usb else {throw StudioError("USB 会话已断开，请重新检测相机。")}
        return try usb.command(text,timeout:timeout)
    }
    private func prepare() throws -> ADBProcess {
        if let adb {return adb}
        try require(FileManager.default.isExecutableFile(atPath:executable.path),"内置 ADB 缺失，安装尚未开始，请重新解压 App。")
        let fingerprint=try command("sha256sum /factory_data/settings.ini",timeout:12000).split(whereSeparator:{$0.isWhitespace}).first.map(String.init) ?? ""
        let model=try identifyCamera(self)
        try require(validHash(fingerprint),"无法确认相机身份。")
        let config=try command("getprop sys.usb.config",timeout:12000)
        try require(["rndis,mass_storage,bulk,acm","rndis,mass_storage,bulk,acm,adb"].contains(config),"当前 USB 模式不受支持，安装尚未开始。")
        originalUSB=config
        let process=ADBProcess(executable)
        log("正在准备文件传输通道")
        _=try process.run(["start-server"],timeout:90,selected:false)
        if !config.split(separator:",").contains("adb") {
            switched=true
            let enable="/system/bin/toybox nohup /system/bin/sh -c 'sleep 1;setprop sys.usb.config none;sleep 1;setprop sys.usb.config rndis,mass_storage,bulk,acm,adb' >/dev/null 2>&1 &"
            do {_=try command(enable,timeout:5000)} catch {
                usb=nil
                // Only this fixed, delayed gadget switch may lose its final USB reply.
                let message=error.localizedDescription
                if !message.contains("LIBUSB_ERROR_NO_DEVICE") && !message.contains("LIBUSB_ERROR_TIMEOUT") {throw error}
            }
        }
        usb=nil
        let end=Date().addingTimeInterval(40)
        while Date()<end {
            let data=try process.run(["devices","-l"],timeout:5,selected:false)
            let candidates=physicalCameras(String(decoding:data,as:UTF8.self))
            try require(candidates.count<=1,"检测到多台相机，请只连接一台。")
            if let candidate=candidates.first {
                process.serial=candidate
                guard let fresh=try? USBLink(log:trace) else {Thread.sleep(forTimeInterval:0.25);continue}
                let freshModel=try identifyCamera(fresh)
                let hash=try fresh.command("sha256sum /factory_data/settings.ini").split(whereSeparator:{$0.isWhitespace}).first.map(String.init)
                try require(freshModel==model && hash==fingerprint,"文件传输通道对应的相机发生变化，已停止操作。")
                try require(try fresh.command("getprop sys.usb.state").split(separator:",").contains("adb"),"文件传输通道尚未生效。")
                usb=fresh;adb=process;log("文件传输通道已就绪");return process
            }
            Thread.sleep(forTimeInterval:0.25)
        }
        throw StudioError("等待相机文件传输通道超时，安装尚未开始。")
    }
    private func validPath(_ path:String) throws {
        try require(path.range(of:"^/[A-Za-z0-9/_.-]+$",options:.regularExpression) != nil,"文件传输路径无效。")
    }
    func readFile(_ path:String,size:Int) throws -> Data {
        try validPath(path);try require((0...4*1024*1024).contains(size),"文件大小无效。")
        let process=try prepare()
        let dir=FileManager.default.temporaryDirectory.appendingPathComponent("startup-download-"+UUID().uuidString)
        try FileManager.default.createDirectory(at:dir,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        defer {try? FileManager.default.removeItem(at:dir)}
        let file=dir.appendingPathComponent("data")
        _=try process.run(["pull",path,file.path])
        let data=try Data(contentsOf:file)
        try require(data.count==size,"相机文件读取不完整，未继续安装。")
        let expected=try command("sha256sum "+path,timeout:12000).split(whereSeparator:{$0.isWhitespace}).first.map(String.init)
        try require(expected==digest(data),"相机文件读回校验失败。")
        log(path==CameraPaths.library ? "原厂备份读取完成，校验通过":"图片读回校验通过")
        return data
    }
    func writeFile(_ data:Data,to path:String) throws {
        try validPath(path);let process=try prepare()
        let dir=FileManager.default.temporaryDirectory.appendingPathComponent("startup-upload-"+UUID().uuidString)
        try FileManager.default.createDirectory(at:dir,withIntermediateDirectories:true,attributes:[.posixPermissions:0o700])
        defer {try? FileManager.default.removeItem(at:dir)}
        let file=dir.appendingPathComponent("data");try data.write(to:file)
        try FileManager.default.setAttributes([.posixPermissions:0o600],ofItemAtPath:file.path)
        _=try process.run(["push",file.path,path])
        let expected=try command("sha256sum "+path,timeout:12000).split(whereSeparator:{$0.isWhitespace}).first.map(String.init)
        try require(expected==digest(data),"相机文件上传校验失败。")
        trace("ADB 文件上传 \(data.count) 字节；SHA256 核对通过")
    }
    func finish() throws {
        guard switched,let originalUSB else {adb=nil;usb=nil;return}
        log("正在恢复相机 USB 连接状态")
        let restore="/system/bin/toybox nohup /system/bin/sh -c 'sleep 1;setprop sys.usb.config none;sleep 1;setprop sys.usb.config \(originalUSB)' >/dev/null 2>&1 &"
        var sent=false
        if let usb {sent=(try? usb.command(restore,timeout:5000)) != nil}
        adb=nil;usb=nil
        let end=Date().addingTimeInterval(25)
        while Date()<end {
            if let fresh=try? USBLink(log:trace) {
                let config=try? fresh.command("getprop sys.usb.config"),state=try? fresh.command("getprop sys.usb.state")
                if config==originalUSB && state==originalUSB {
                    switched=false;usb=nil;log("USB 连接状态已恢复");return
                }
                if !sent && config=="rndis,mass_storage,bulk,acm,adb" {sent=(try? fresh.command(restore,timeout:5000)) != nil}
            }
            Thread.sleep(forTimeInterval:0.25)
        }
        throw StudioError("USB 原状态恢复未确认；请重启相机后重新检测。")
    }
}
