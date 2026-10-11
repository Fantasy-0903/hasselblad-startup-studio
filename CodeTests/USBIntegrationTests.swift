import Foundation
@main struct USBIntegrationTests {
    static func main() throws {
        sc_test_initialize(CommandLine.arguments[1])
        var cases=0
        for mode in [0,1,2,3,4] {
            sc_test_scenario(Int32(mode));var messages=[String]()
            do {
                let link=try USBLink(log:{messages.append($0)})
                try require(try link.command("getprop ro.product.device",timeout:100)=="eagle2_ec1706_native","Swift bridge lost USB response")
            }
            try require(messages.contains("阶段：机型检测") && messages.contains(where:{$0.contains("完成；RX=")}) && messages.last=="USB: 会话已关闭","Swift C logging callback lost stages or lifetime")
            cases+=1
        }
        for (mode,token) in [(6,"LIBUSB_ERROR_NO_DEVICE"),(7,"状态 7"),(5,"LIBUSB_ERROR_TIMEOUT")] {
            sc_test_scenario(Int32(mode));var messages=[String]()
            let link=try USBLink(log:{messages.append($0)})
            var failed=false
            do {_=try link.command("getprop ro.product.device",timeout:10)} catch {
                try require(error.localizedDescription.contains("机型检测") && error.localizedDescription.contains(token),"Failure lost stage or USB diagnostic");failed=true
            }
            try require(failed && messages.contains(where:{$0.contains(token)}),"USB error not delivered to Swift log");cases+=1
        }
        for (mode,token) in [(13,"未检测到"),(14,"LIBUSB_ERROR_BUSY"),(15,"端点检查")] {
            sc_test_scenario(Int32(mode));var messages=[String]();var failed=false
            do {_=try USBLink(log:{messages.append($0)})} catch {try require(error.localizedDescription.contains(token),"USB open error lost diagnostic");failed=true}
            try require(failed && messages.contains(where:{$0.contains(token)}),"USB opening callback lost error");cases+=1
        }
        print("PASS: \(cases) Swift/C transport and logging integration cases; no hardware accessed")
    }
}
