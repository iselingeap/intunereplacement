using ConsoleApp1.Entiteiten;
using ConsoleApp1.Service;
using System.Net;
using System.Text;


internal class Program
{

    private static async Task Main(string[] args)
    {
        var dm = new Datamanagement();
        var httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://localhost:7034/");
        dm.StartConnection();
        httpListener.Start();

        while (true)
        {
            var context = await httpListener.GetContextAsync();
            Console.WriteLine("Client connected.");
            Task.Factory.StartNew(() => HandleHttpClient(context, dm));
        }
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