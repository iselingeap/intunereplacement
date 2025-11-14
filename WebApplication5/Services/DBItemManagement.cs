using Microsoft.Data.SqlClient;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.VisualBasic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using System;

namespace WebApplication5.Services
{
    public class DBItemManagement
    {
        string connectionString = "";

        // Optional DI configuration holder (if injected)
        private readonly IConfiguration? _configuration;

        // Constructor used when IConfiguration is available via DI
        public DBItemManagement(IConfiguration configuration)
        {
            _configuration = configuration;
            connectionString = _configuration.GetConnectionString("MonitoringApp") ?? "";
        }

        // Parameterless constructor fallback:
        // Loads appsettings.json from the application's base directory if present.
        public DBItemManagement()
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                var config = builder.Build();
                connectionString = config.GetConnectionString("MonitoringApp") ?? "";
                _configuration = config;
            }
            catch
            {
                // If configuration cannot be built, leave connectionString as empty
                connectionString = "";
            }
        }

        public string GetConnectionString()
        {
            return connectionString;
        }

        public void SetConnectionString(string newConnectionString)
        {
            connectionString = newConnectionString;
        }

        public void TestConnection()
        {
            using (var connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    Console.WriteLine("Connection successful.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Connection failed: {ex.Message}");
                }
            }
        }

        public bool ValidateUser(string gebruikers, string wachtwoord)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                string query = "SELECT COUNT(1) FROM Gebruikers WHERE gebruikersnaam = @gebruikersnaam AND wachtwoord = @wachtwoord AND IsAdmin = 1";
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@gebruikersnaam", gebruikers);
                    command.Parameters.AddWithValue("@wachtwoord", wachtwoord);

                    connection.Open();
                    int count = (int)command.ExecuteScalar();
                    return count > 0;
                }
            }
        }

        public List<Dictionary<string, object>> GetAllLaptopData(List<string> gadColumns, [Optional] string sorter )
        {
            var getAllData = new List<Dictionary<string, object>>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand($"SELECT * FROM Laptops {sorter}", connection))
                using (var reader = command.ExecuteReader())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        gadColumns.Add(reader.GetName(i));
                    }
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();
                        foreach (var col in gadColumns)
                        {
                            row[col] = reader[col];
                        }
                        getAllData.Add(row);
                    }
                }
            }
            return getAllData;
        }

        public List<Dictionary <string, object>> GetAllAppData( List<string> gadColumns, string sorter)
        {
            var getAllData = new List<Dictionary<string, object>>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand($"SELECT DISTINCT  name, app_id, installedVersion, AvailableVersions, IsUpdateAvailable, pinned FROM Apps {sorter}", connection))
                using (var reader = command.ExecuteReader())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        gadColumns.Add(reader.GetName(i));
                    }
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();
                        foreach (var col in gadColumns)
                        {
                            row[col] = reader[col];
                        }
                        getAllData.Add(row);
                    }
                }
            }
            return getAllData;
        }

        public List<Dictionary<string, object>> GetAllLaptopsUsingApp( List<string> gadColumns, string AppId, string Version)
        {
            var getAllData = new List<Dictionary<string, object>>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand($"SELECT Laptops.id, Laptops.name, Laptops.os, Laptops.osVer, Laptops.lastUpdate, Laptops.MAC, Laptops.HWID, Laptops.GUID FROM Laptops JOIN Apps ON Laptops.id = Apps.laptop_id WHERE Apps.app_id = @appId AND Apps.installedVersion = @version", connection))
                {
                    command.Parameters.AddWithValue("@appId", AppId);
                    command.Parameters.AddWithValue("@version", Version ?? string.Empty);
                    using (var reader = command.ExecuteReader())
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            gadColumns.Add(reader.GetName(i));
                        }
                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            foreach (var col in gadColumns)
                            {
                                row[col] = reader[col];
                            }
                            getAllData.Add(row);
                        }
                    }
                }
            }
            return getAllData;

        }

        public List<Dictionary<string, object>> GetAllAppsOnLaptop( List<string> gadColumns, string LaptopId,string sorter = "")
        {
            var getAllData = new List<Dictionary<string, object>>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand($"SELECT DISTINCT id, name, app_id, installedVersion, AvailableVersions, IsUpdateAvailable FROM Apps WHERE laptop_id = @laptopId {sorter} ", connection))
                {
                    command.Parameters.AddWithValue("@laptopId", LaptopId);
                    using (var reader = command.ExecuteReader())
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            gadColumns.Add(reader.GetName(i));
                        }
                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            foreach (var col in gadColumns)
                            {
                                row[col] = reader[col];
                            }
                            getAllData.Add(row);
                        }
                    }
                }
            }
            return getAllData;
        }

        public List<Dictionary<string, object>> GetLaptops( string query)
        {
            var Data = new List<Dictionary<string, object>>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        do
                        {
                            var row = new Dictionary<string, object>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                row[reader.GetName(i)] = reader.GetValue(i);
                            }
                            Data.Add(row);
                        } while (reader.Read());
                    } 
                }
            }
            return Data;
        }

        public List<Dictionary<string, object>> GetAppsByAppId(string appId)
        {
            var results = new List<Dictionary<string, object>>();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("SELECT * FROM Apps WHERE app_id = @app_id", connection))
                {
                    command.Parameters.AddWithValue("@app_id", appId ?? string.Empty);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.HasRows)
                            return results;

                        var columns = new List<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            columns.Add(reader.GetName(i));
                        }

                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            foreach (var col in columns)
                            {
                                row[col] = reader[col];
                            }
                            results.Add(row);
                        }
                    }
                }
            }
            return results;
        }

        public bool ToggleAppPinnedStatus(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentException("appId must be provided", nameof(appId));

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                bool currentPinned;
                using (var selectCmd = new SqlCommand("SELECT pinned FROM Apps WHERE app_id = @app_id", connection))
                {
                    selectCmd.Parameters.AddWithValue("@app_id", appId);
                    using (var reader = selectCmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException($"No app found with app_id = '{appId}'.");

                        var val = reader["pinned"];
                        currentPinned = val == DBNull.Value ? false : Convert.ToBoolean(val);
                    }
                }

                bool newPinned = !currentPinned;

                using (var updateCmd = new SqlCommand("UPDATE Apps SET pinned = @IsPinned WHERE app_id = @app_id", connection))
                {
                    updateCmd.Parameters.AddWithValue("@IsPinned", newPinned);
                    updateCmd.Parameters.AddWithValue("@app_id", appId);
                    updateCmd.ExecuteNonQuery();
                }

                return newPinned;
            }
        }

        public void SaveUserCreatedTask(string taskName, string Task)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("INSERT INTO UserMadeTasks (name, date, powerShellScript) VALUES (@Name, GETDATE(), @PowerShellScript)", connection))
                {
                    command.Parameters.AddWithValue("@Name", taskName);
                    command.Parameters.AddWithValue("@PowerShellScript", Task);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void ManuallTaskCreationTask(string Task, List<string> laptopsIds, string taskname)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("INSERT INTO Tasks (name, creationDate, task, isPSCommand, status, laptop_id) VALUES (@name,GETDATE(), @Task, 1, 1, @LaptopId)", connection))
                {
                    command.Parameters.AddWithValue("@Task", Task);
                    command.Parameters.AddWithValue("@Name", taskname);
                    foreach (var laptopId in laptopsIds)
                    {
                        command.Parameters.AddWithValue("@LaptopId", laptopId);
                        command.ExecuteNonQuery();
                        command.Parameters.RemoveAt("@LaptopId");
                    }
                }
            }
        }

        public void getAllUserCreatedTasks(List<string> gadColumns, List<Dictionary<string, object>> getAllData)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("SELECT * FROM UserMadeTasks", connection))
                using (var reader = command.ExecuteReader())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        gadColumns.Add(reader.GetName(i));
                    }
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();
                        foreach (var col in gadColumns)
                        {
                            row[col] = reader[col];
                        }
                        getAllData.Add(row);
                    }
                }
            }
        }

        public void DeleteUserCreatedTask(int taskId)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("DELETE FROM UserMadeTasks WHERE id = @TaskId", connection))
                {
                    command.Parameters.AddWithValue("@TaskId", taskId);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SendScriptToTaskSystem(int taskId, List<string> laptops_ids, string taskname)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (var laptopId in laptops_ids)
                {
                    using (var command = new SqlCommand("INSERT INTO Tasks (name,creationDate, task, isPSCommand, status, laptop_id) SELECT @Name, GETDATE(), powerShellScript, 1, 1, @LaptopId FROM UserMadeTasks WHERE id = @TaskId", connection))
                    {
                        command.Parameters.AddWithValue("@TaskId", taskId);
                        command.Parameters.AddWithValue("@LaptopId", laptopId);
                        command.Parameters.AddWithValue("@Name", taskname);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        private string GetConnectionStringInternal() => GetConnectionString();

        public Dictionary<string, object>? GetUserCreatedTaskById(int id)
        {
            var result = new Dictionary<string, object>();
            var cs = GetConnectionStringInternal();
            if (string.IsNullOrEmpty(cs)) return null;

            using (var conn = new SqlConnection(cs))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, name, powerShellScript FROM UserMadeTasks WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);

                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                {
                    if (!rdr.Read()) return null;

                    result["id"] = rdr["id"];
                    result["name"] = rdr["name"] == DBNull.Value ? "" : rdr["name"].ToString() ?? "";
                    result["powerShellScript"] = rdr["powerShellScript"] == DBNull.Value ? "" : rdr["powerShellScript"].ToString() ?? "";

                    return result;
                }
            }
        }

        public List<Dictionary<string, object>> GetAllTasks()
        {
            var results = new List<Dictionary<string, object>>();
            var cs = GetConnectionStringInternal();
            if (string.IsNullOrEmpty(cs)) return results;
            using (var conn = new SqlConnection(cs))
            using (var cmd = conn.CreateCommand())
            {
               cmd .CommandText = "SELECT id, name ,creationDate, task, isPSCommand, status, laptop_id ,ClientResponse FROM Tasks";
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var row = new Dictionary<string, object>();
                        row["id"] = rdr["id"];
                        row["name"] = rdr["name"] == DBNull.Value ? "" : rdr["name"].ToString() ?? "";
                        row["creationDate"] = rdr["creationDate"];
                        row["task"] = rdr["task"] == DBNull.Value ? "" : rdr["task"].ToString() ?? "";
                        row["isPSCommand"] = rdr["isPSCommand"];
                        row["status"] = rdr["status"];
                        row["laptop_id"] = rdr["laptop_id"] == DBNull.Value ? "" : rdr["laptop_id"].ToString() ?? "";
                        row["ClientResponse"] = rdr["ClientResponse"] == DBNull.Value ? "" : rdr["ClientResponse"].ToString() ?? "";
                        results.Add(row);
                    }
                }
            }
            return results;
        }

        // this sitting here because blazer won't stop yelling about it in the razor pages
        public string statusConvert(string status)
        {
            return status switch
            {
                "0" => "Pending",
                "1" => "In Progress",
                "2" => "Completed",
                _ => "Unknown"
            };
        }

    }
}
