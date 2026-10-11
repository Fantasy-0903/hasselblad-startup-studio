import Foundation

// Explicitly invoked hardware acceptance entry. No persistent installation occurs.
@main struct LiveTransferTests {
    static func main() {
        do {try run()} catch {FileHandle.standardError.write(Data(("FAIL "+error.localizedDescription+"\n").utf8));exit(1)}
    }
    static func run() throws {
        try require(CommandLine.arguments.count==4 && CommandLine.arguments[1]=="--live-transfer","需显式传入 --live-transfer、App Resources 和本地输出目录。")
        let resources=URL(fileURLWithPath:CommandLine.arguments[2]),out=URL(fileURLWithPath:CommandLine.arguments[3])
        try FileManager.default.createDirectory(at:out,withIntermediateDirectories:true)
        let detail=out.appendingPathComponent("transport.log")
        FileManager.default.createFile(atPath:detail.path,contents:nil)
        let handle=try FileHandle(forWritingTo:detail);defer {try? handle.close()}
        let link=try ManagedCameraLink(resources:resources,log:{print($0)},trace:{try? handle.write(contentsOf:Data(($0+"\n").utf8))})
        let controller=try CameraController(link:link,resources:resources,backupRoot:out.appendingPathComponent("backup"))
        var root:String?,record:RecoveryRecord?
        let start=Date()
        do {
            let (before,old)=try controller.inspect()
            try require(old==nil && before.libraryHash==Adapter.stockHash,"实机传输测试要求相机处于干净原厂状态。")
            for n in 1...3 {
                let bytes=try controller.read(CameraPaths.library)
                try require(bytes.count==2051168 && digest(bytes)==Adapter.stockHash,"原厂库完整读取校验失败。")
                print("PASS 完整原厂库读取 \(n)/3")
            }
            let path=try controller.makeRoot();root=path
            let r=RecoveryRecord(snapshot:before,adapter:controller.adapter,root:path);record=r
            try controller.createRoot(path,record:r)
            let image=GrayImage(width:1024,height:768,pixels:Data((0..<1024*768).map {UInt8(truncatingIfNeeded:($0&*73)^($0>>7))}))
            let bytes=try image.envelope()
            for n in 1...3 {
                try controller.upload(bytes,to:path+"/image")
                try require(try controller.read(path+"/image")==bytes,"最大尺寸图片上传／读回不一致。")
                print("PASS 1024×768 灰度资源往返 \(n)/3")
            }
            try controller.cleanRoot(path,record:r);root=nil
            try require(try controller.snapshot()==before,"传输测试改变了原厂状态。")
            try link.finish()
            let fresh=try CameraController(link:USBLink(),resources:resources,backupRoot:out.appendingPathComponent("backup"))
            try require(try fresh.snapshot()==before && !fresh.exists(path),"通道恢复后原厂状态或暂存文件核对失败。")
            let report:[String:Any]=["passed":true,"deviceAccessed":true,"fullLibraryReads":3,"maximumImageRoundTrips":3,"seconds":Date().timeIntervalSince(start),"factoryUnchanged":true,"temporaryFilesRemoved":true,"usbRestored":true,"persistentInstall":false]
            try JSONSerialization.data(withJSONObject:report,options:.prettyPrinted).write(to:out.appendingPathComponent("report.json"))
            print("PASS 原厂状态、暂存文件清理、USB 恢复；用时 \(Int(Date().timeIntervalSince(start))) 秒")
        } catch {
            if let root,let record {try? controller.cleanRoot(root,record:record)}
            do {try link.finish()} catch {print("USB 恢复错误："+error.localizedDescription)}
            throw error
        }
    }
}
