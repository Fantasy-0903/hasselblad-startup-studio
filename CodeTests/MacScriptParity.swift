// Offline-only export of the production Mac script generators for Windows parity checks.
import Foundation
@main struct MacScriptParity {
    static func main() throws {
        let resources=URL(fileURLWithPath:CommandLine.arguments[1])
        let adapter=try JSONDecoder().decode(Adapter.self,from:Data(contentsOf:resources.appendingPathComponent("adapter.json")))
        var results=[[String:String]]()
        for index in 1...5 {
            let object=try JSONSerialization.jsonObject(with:Data(contentsOf:resources.appendingPathComponent("test-scripts-\(index).json"))) as! [String:Any]
            let record=try JSONDecoder().decode(RecoveryRecord.self,from:JSONSerialization.data(withJSONObject:object["record"]!))
            let pixels=Data((0..<321*239).map {UInt8($0%251)})
            let image=try GrayImage(width:321,height:239,pixels:pixels).envelope()
            results.append([
                "install":Scripts.install(record:record,adapter:adapter,imageHash:digest(image),root:record.roots[0],current:adapter.originalSHA256),
                "update":Scripts.install(record:record,adapter:adapter,imageHash:digest(image),root:record.roots[0],current:adapter.adapterSHA256),
                "restore":Scripts.restore(record:record,adapter:adapter,root:record.roots[0])])
        }
        try JSONSerialization.data(withJSONObject:results,options:[.sortedKeys]).write(to:URL(fileURLWithPath:CommandLine.arguments[2]))
    }
}
