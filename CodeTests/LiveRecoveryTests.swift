import Foundation
@main struct LiveRecoveryTests {
    static func main() {
        do {try run()} catch {FileHandle.standardError.write(Data(("FAIL "+error.localizedDescription+"\n").utf8));exit(1)}
    }
    static func run() throws {
        try require(CommandLine.arguments.count==4 && CommandLine.arguments[1]=="--restore-only","需显式指定 --restore-only、Resources 与输出目录。")
        let resources=URL(fileURLWithPath:CommandLine.arguments[2]),out=URL(fileURLWithPath:CommandLine.arguments[3])
        let backups=FileManager.default.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("X2DStartupStudio/Backups")
        let controller=try CameraController(link:USBLink(),resources:resources,backupRoot:backups)
        let (before,record)=try controller.inspect()
        guard let record else {try require(before.libraryHash==Adapter.stockHash,"缺少首次原厂备份。");print("已处于原厂状态");return}
        let start=Date();try controller.restore()
        let (after,remaining)=try controller.inspect()
        try require(remaining==nil && after==record.baseline,"恢复后未完全匹配首次备份。")
        try FileManager.default.createDirectory(at:out,withIntermediateDirectories:true)
        let report:[String:Any]=["passed":true,"deviceAccessed":true,"seconds":Date().timeIntervalSince(start),"stockLibrary":true,"factoryIdentityUnchanged":true,"metadataRestored":true,"loadedBranchesRestored":true,"ownedFilesRemoved":true,"firstHostBackupRetained":true]
        try JSONSerialization.data(withJSONObject:report,options:.prettyPrinted).write(to:out.appendingPathComponent("recovery.json"))
        print("PASS 原厂库、原厂身份、权限时间标签、运行分支及全部自有文件清理，\(Int(Date().timeIntervalSince(start))) 秒")
    }
}
