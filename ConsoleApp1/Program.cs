using ConsoleApp1.Service;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Linq;


internal class Program
{

    private static async Task Main(string[] args)
    {
        var dm = new Datamanagement();
        var httpListener = new HttpListener();

        string ip = GetLocalIPv4Address();
        string prefix = $"http://{ip}:7034/";
        httpListener.Prefixes.Add(prefix);

        dm.StartConnection();
        Console.WriteLine($"Listening on {prefix}");
        httpListener.Start();

        while (true)
        {
            var context = await httpListener.GetContextAsync();
            Console.WriteLine("Client connected.");
            Task.Factory.StartNew(() => HandleHttpClient(context, dm));
        }
    }

    private static string? GetLocalIPv4Address()
    {
        // Prefer active network interfaces with an IPv4 unicast address that's not loopback.
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                 n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var props = ni.GetIPProperties();
            var addr = props.UnicastAddresses
                            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                            .Select(a => a.Address)
                            .FirstOrDefault(a => !IPAddress.IsLoopback(a));
            if (addr != null)
                return addr.ToString();
        }

        // Fallback: try DNS host entry
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null)
                return ip.ToString();
        }
        catch
        {
            // ignore and fall through to null
        }

        return null;
    }

    private static async Task HandleHttpClient(HttpListenerContext context, Datamanagement dm)
    {
        var request = context.Request;
        var method = request.HttpMethod;
        string type = context.Request.RawUrl;

        using (RequestDispatcher.HandleAsync(context, dm))
        {
            return;
        }
    }

}