using ConsoleApp1.Service;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using ConsoleApp1.Entiteiten;
using System.Net.Sockets;
using Microsoft.IdentityModel.Tokens;
using System.Threading.Tasks;


internal class Program
{
    private static TcpListener? server;
    private const string ip = "192.168.1.6";
    private const int port = 5000;


    private static async Task Main(string[] args)
    {
        Console.WriteLine("Getting Connection ...");

        var datasource = @"(localdb)\MSSQLLocalDB";//server
        var database = "MonitoringApp"; //database
        var username = @"johnny"; //username
        var password = "test"; //password

        string connString = @"Data Source=" + datasource + ";Initial Catalog="
                    + database + ";User ID=" + username + ";Password=" + password;

        SqlConnection conn = new SqlConnection(connString);
        SqlCommand cmd = new SqlCommand();
        Datamanagement dm = new Datamanagement();

        try
        {
            Console.WriteLine("Openning Connection ...");

            conn.Open();

            Console.WriteLine("Connection successful!");
        }
        catch (Exception e)
        {
            Console.WriteLine("Error: " + e.Message);
        }


        TcpListener server =  new TcpListener(IPAddress.Parse(ip) , port);
        server.Start();
        Console.WriteLine(IPAddress.Parse(ip));
        Console.WriteLine($"Server started on port {port}. Waiting for a connection...");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            Console.WriteLine("Client connected.");
            _ = HandleClientAsync(client, conn, cmd, dm);
        }
    }

    private static async Task HandleClientAsync(TcpClient client, SqlConnection conn, SqlCommand cmd, Datamanagement dm)
    {
        using NetworkStream stream = client.GetStream();
        using MemoryStream ms = new MemoryStream();
        byte[] buffer = new byte[8192];
        int bytesRead;
        // Read until client closes connection or stream ends
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, bytesRead);
            if (!stream.DataAvailable)
                break;
        }
        string json = Encoding.UTF8.GetString(ms.ToArray());
        File.WriteAllText("C:\\Windows\\Temp\\MA\\iloveprotcals.json", json);
        Console.WriteLine(json);
        while (true)
        {
            if (json.StartsWith("{") && json.EndsWith("}"))
                break;

            else
            {
                json = json.Remove(0, 1);
            }
        }
        cmd.Connection = conn;
        DevicesData dataTest = dm.convertJsonToSql(json);
        cmd.CommandText = "SELECT COUNT(*) FROM Laptops WHERE MAC = @Mac";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@Mac", dataTest.mac ?? (object)DBNull.Value);
        int count = (int)cmd.ExecuteScalar();
        if (count > 0)
        {
            // Update existing record (except MAC)
            cmd.CommandText = @"UPDATE Laptops 
            SET name = @name, os = @os, osVer = @osVer, lastUpdate = @lastUpdate 
            WHERE MAC = @Mac";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@name", dataTest.Name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@os", dataTest.Os ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@osVer", dataTest.OsVer ?? (object)DBNull.Value);
            var sqlMinDate = new DateTime(1753, 1, 1);
            cmd.Parameters.AddWithValue("@lastUpdate",
                dataTest.LastUpdate < sqlMinDate ? (object)DBNull.Value : dataTest.LastUpdate);
            cmd.Parameters.AddWithValue("@Mac", dataTest.mac ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
            Console.WriteLine("Existing record updated.");
            cmd.Parameters.Clear();

        }
        else
        {
            // Insert new record
            cmd.CommandText = @"INSERT INTO Laptops (name, os, osVer, lastUpdate, MAC) 
                                                        VALUES (@name, @os, @osVer, @lastUpdate, @Mac)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@name", dataTest.Name ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@os", dataTest.Os ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@osVer", dataTest.OsVer ?? (object)DBNull.Value);
            var sqlMinDate = new DateTime(1753, 1, 1);
            cmd.Parameters.AddWithValue("@lastUpdate",
                dataTest.LastUpdate < sqlMinDate ? (object)DBNull.Value : dataTest.LastUpdate);
            cmd.Parameters.AddWithValue("@Mac", dataTest.mac ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
            Console.WriteLine("New record inserted.");
        }
        // After updating/inserting into Laptops table, update Apps table for the same laptop
        // Assume Laptops table has an 'id' column (primary key) and MAC is unique

        // Get the laptop id using MAC
        cmd.CommandText = "SELECT id FROM Laptops WHERE MAC = @Mac";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@Mac", dataTest.mac ?? (object)DBNull.Value);
        object laptopIdObj = cmd.ExecuteScalar();
        if (laptopIdObj != null && int.TryParse(laptopIdObj.ToString(), out int laptopId))
        {
            // Remove existing apps for this laptop
            cmd.CommandText = "DELETE FROM Apps WHERE id = @LaptopId";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@LaptopId", laptopId);
            cmd.ExecuteNonQuery();

            // Insert new apps from appsInfo list
            if (dataTest.appsInfo != null)
            {
                foreach (var app in dataTest.appsInfo)
                {
                    cmd.CommandText = @"INSERT INTO Apps (id, name, app_id, installedVersion, IsUpdateAvailable, AvailableVersions)
                                VALUES (@LaptopId, @AppName, @AppId, @InstalledVersion, @IsUpdateAvailable, @AvailableVersions)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                    cmd.Parameters.AddWithValue("@AppName", app.Name ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@AppId", app.Id ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@InstalledVersion", app.Id ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@IsUpdateAvailable", app.IsUpdateAvailable);
                    cmd.Parameters.AddWithValue("@AvailableVersions", app.AvailableVersions != null ? string.Join(",", app.AvailableVersions) : (object)DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

}