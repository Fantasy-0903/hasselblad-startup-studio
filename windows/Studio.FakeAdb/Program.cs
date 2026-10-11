using System.Net;
using System.Net.Sockets;
using System.Text;

// Offline process fixture. This executable has no USB or ADB dependencies.
Console.OutputEncoding = new UTF8Encoding(false);
if (args.Length == 4 && args[0] == "-L" && args[2] == "nodaemon" && args[3] == "server")
{
    if (Environment.GetEnvironmentVariable("STARTUP_STUDIO_FAKE_FAIL") == "1") return 1;
    var port = int.Parse(args[1].Split(':')[^1]);
    using var listener = new TcpListener(IPAddress.Loopback, port); listener.Start();
    while (true) { using var client = listener.AcceptTcpClient(); }
}
if (args.Length >= 4 && args[0] == "-H") { Console.Write(args[1] + ":" + args[3]); return 0; }
if (args.Length > 0 && args[0] == "-d") args = args[1..];
switch (args.FirstOrDefault())
{
    case "large": using (var output = Console.OpenStandardOutput()) output.Write(new byte[262144]); break;
    case "denied": Console.Error.Write("Permission denied"); return 1;
    case "many": Console.Error.Write("more than one device/emulator"); return 1;
    case "slow": Thread.Sleep(3000); break;
    case "argument": Console.Write(args[1]); break;
}
return 0;
