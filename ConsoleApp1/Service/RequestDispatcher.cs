using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ConsoleApp1.Entiteiten;
using ConsoleApp1.Service;

namespace ConsoleApp1.Service
{
    public static class RequestDispatcher
    {
        private const string DefaultKey = "129089adf920139asa123bda";

        public static async Task HandleAsync(HttpListenerContext context, Datamanagement dm)
        {
            var request = context.Request;
            var rawUrl = request.RawUrl ?? string.Empty;

            Console.WriteLine($"Incoming: {rawUrl}");

            var (version, path) = ParseVersion(rawUrl);

            switch (version)
            {
                case 2:
                    await HandleV2(context, path, request, dm);
                    Console.WriteLine("V2 Active");
                    break;
                default:
                    await HandleV1(context, path, request, dm);
                    Console.WriteLine("V1 Backup Active");
                    break;
            }
        }

        // Parse versioned paths like "/application/v2/AreThereTasks?id=..."
        private static (int version, string normalizedPath) ParseVersion(string rawUrl)
        {
            const string prefix = "/application/v";
            if (rawUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // after "/application/v" there is a number then '/'
                var after = rawUrl.Substring(prefix.Length);
                int slashIndex = after.IndexOf('/');
                if (slashIndex > 0)
                {
                    var verStr = after.Substring(0, slashIndex);
                    if (int.TryParse(verStr, out int ver))
                    {
                        // normalized path should be "/application/" + remainder
                        var remainder = after.Substring(slashIndex + 1);
                        var normalized = "/application/" + remainder;
                        // preserve any leading slash in remainder segments
                        if (!normalized.StartsWith("/")) normalized = "/" + normalized;
                        return (ver, normalized);
                    }
                }
            }
            return (1, rawUrl);
        }

        // Version 1 
        private static async Task HandleV1(HttpListenerContext context, string path, HttpListenerRequest request, Datamanagement dm)
        {
            try
            {
                if (path.StartsWith("/application/AreThereTasks?id=", StringComparison.OrdinalIgnoreCase))
                {
                    var idEnc = path.Substring("/application/AreThereTasks?id=".Length);
                    var id = dm.DecryptString(idEnc, DefaultKey);
                    Console.WriteLine(id);
                    var updates = dm.GetUserTasks(id);
                    Console.WriteLine(updates);
                    var responseEncrypted = dm.EncryptString(updates, DefaultKey);
                    var buffer = Encoding.UTF8.GetBytes(responseEncrypted);
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, buffer);
                }
                else if (path.Equals("/application/jsonDataLog", StringComparison.OrdinalIgnoreCase))
                {
                    var body = await ReadRequestBodyAsync(request);
                    var json = dm.DecryptString(body, DefaultKey);
                    Console.WriteLine("Received data" + json);
                    DevicesData data = dm.ConvertJsonToSql(json);
                    dm.InsertOrUpdateDatabase(data);
                    dm.CompareClientPinnedAppsToServer(json);
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, Array.Empty<byte>());
                }
                else if (path.StartsWith("/application/WhoIsClient?hwid=", StringComparison.OrdinalIgnoreCase))
                {
                    var hwidEnc = path.Substring("/application/WhoIsClient?hwid=".Length);
                    var hwid = dm.DecryptString(hwidEnc, DefaultKey);
                    string userGuid = dm.GetUserGUIDbyHWID(hwid);
                    var responseString = dm.EncryptString(userGuid, DefaultKey);
                    var buffer = Encoding.UTF8.GetBytes(responseString);
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, buffer);
                }
                else if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                         path.Equals("/application/TasksReturnFromClient", StringComparison.OrdinalIgnoreCase))
                {
                    var body = await ReadRequestBodyAsync(request);
                    var json = dm.DecryptString(body, DefaultKey);
                    
                    Console.WriteLine("Received TasksReturnFromClient:" + json);
                    dm.UpdatetaskBasedOnResponse(json);
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, Array.Empty<byte>());
                }
                else
                {

                    await WriteResponseAsync(context, (int)HttpStatusCode.BadRequest, Array.Empty<byte>());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Handler V1 error: " + ex);
                await WriteResponseAsync(context, (int)HttpStatusCode.InternalServerError, Encoding.UTF8.GetBytes("error"));
            }
        }

        // Version 2. same endpoints but logs version and could use different key/format
        private static async Task HandleV2(HttpListenerContext context, string path, HttpListenerRequest request, Datamanagement dm)
        {
            try
            {
                Console.WriteLine("Dispatching to V2 handlers for path: " + path);

                var key = DefaultKey;

                if (path.StartsWith("/application/AreThereTasks?id=", StringComparison.OrdinalIgnoreCase))
                {
                    var idEnc = path.Substring("/application/AreThereTasks?id=".Length);
                    var id = dm.DecryptString(idEnc, key);
                    var updates = dm.GetUserTasks(id);
                    Console.WriteLine("[v2] " + updates);
                    var responseEncrypted = dm.EncryptString(updates, key);
                    var buffer = Encoding.UTF8.GetBytes(responseEncrypted);
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, buffer);
                }
                else if (path.Equals("/application/jsonDataLog", StringComparison.OrdinalIgnoreCase))
                {
                    var body = await ReadRequestBodyAsync(request);
                    var json = dm.DecryptString(body, key);
                    Console.WriteLine("[v2] Received data");
                    DevicesData data = dm.ConvertJsonToSql(json);
                    dm.InsertOrUpdateDatabase(data);
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, Array.Empty<byte>());
                }
                else if (path.StartsWith("/application/WhoIsClient?hwid=", StringComparison.OrdinalIgnoreCase))
                {
                    var hwidEnc = path.Substring("/application/WhoIsClient?hwid=".Length);
                    var hwid = dm.DecryptString(hwidEnc, key);
                    string userGuid = dm.GetUserGUIDbyHWID(hwid);
                    var responseString = dm.EncryptString(userGuid, key);
                    var buffer = Encoding.UTF8.GetBytes(responseString);
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, buffer);
                }
                else if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                         path.Equals("/application/TasksReturnFromClient", StringComparison.OrdinalIgnoreCase))
                {
                    var body = await ReadRequestBodyAsync(request);
                    var json = dm.DecryptString(body, key);
                    Console.WriteLine("[v2] Received TasksReturnFromClient:" + json);
                        
                    await WriteResponseAsync(context, (int)HttpStatusCode.OK, Array.Empty<byte>());
                }
                else
                {
                    await WriteResponseAsync(context, (int)HttpStatusCode.NotFound, Array.Empty<byte>());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Handler V2 error: " + ex);
                await WriteResponseAsync(context, (int)HttpStatusCode.InternalServerError, Encoding.UTF8.GetBytes("error"));
            }
        }

        // read request body honoring encoding
        private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            return await reader.ReadToEndAsync();
        }

        // write response and close stream
        private static async Task WriteResponseAsync(HttpListenerContext context, int statusCode, byte[] body)
        {
            try
            {
                context.Response.StatusCode = statusCode;
                if (body != null && body.Length > 0)
                {
                    context.Response.ContentLength64 = body.Length;
                    await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
                }
                else
                {
                    context.Response.ContentLength64 = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("WriteResponseAsync error: " + ex);
            }
            finally
            {
                try { context.Response.OutputStream.Close(); } catch { }
                try { context.Response.Close(); } catch { }
                Console.WriteLine("Response closed.");
            }
        }
    }
}