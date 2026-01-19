using ConsoleApp1.Entiteiten;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ConsoleApp1.Service
{

    public class Datamanagement
    {
        SqlConnection conn = new SqlConnection();
        SqlCommand cmd = new SqlCommand();

        public void StartConnection()
        {

            // Locate dbconfig.json
            string configPath = System.IO.Path.Combine(AppContext.BaseDirectory ?? string.Empty, "dbconfig.json");
            if (!System.IO.File.Exists(configPath))
            {
                // Try current directory as fallback
                var alt = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "dbconfig.json");
                if (System.IO.File.Exists(alt))
                    configPath = alt;
            }

            string connString = null;

            if (System.IO.File.Exists(configPath))
            {
                try
                {
                    string json = System.IO.File.ReadAllText(configPath);
                    var jo = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);

                    // Try direct keys first
                    connString = jo?.Value<string>("ConnectionString")
                                 ?? jo?.Value<string>("DefaultConnection");

                    // Try a "ConnectionStrings" object
                    if (string.IsNullOrWhiteSpace(connString) && jo?["ConnectionStrings"] is Newtonsoft.Json.Linq.JObject csObj)
                    {
                        // prefer "DefaultConnection" if present
                        connString = csObj.Value<string>("DefaultConnection");

                        // otherwise take the first property value
                        if (string.IsNullOrWhiteSpace(connString))
                        {
                            var firstProp = csObj.Properties().FirstOrDefault();
                            if (firstProp != null)
                                connString = firstProp.Value?.ToString();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(connString))
                    {
                        Console.WriteLine($"dbconfig.json found at '{configPath}' but no connection string was located. Falling back to hard-coded string.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error reading or parsing dbconfig.json: " + ex.Message);
                    connString = null;
                }
            }
            else
            {
                Console.WriteLine("dbconfig.json not found. Falling back to hard-coded connection string.");
            }

            // Hard-coded fallback (preserve previous value)
            if (string.IsNullOrWhiteSpace(connString))
            {
                connString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=MonitoringApp;User ID=johnny;Password=test";
            }

            conn.ConnectionString = connString;
            cmd.Connection = conn;
            try
            {
                Console.WriteLine("Opening Connection ...");
                conn.Open();
                Console.WriteLine("Connection successful!");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
            }
        }

        public DevicesData ConvertJsonToSql(string json)
        {

            DevicesData data = JsonConvert.DeserializeObject<DevicesData>(json);
            return data;
        }


        public void DeleteOlderTasks()
        {
            cmd.CommandText = "DELETE FROM Tasks WHERE creationDate < @dateLimit";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@dateLimit", DateTime.Now.AddDays(-30));
            int affectedRows = cmd.ExecuteNonQuery();
        }

        public void CompareClientPinnedAppsToServer(string Response)
        {
            DeleteOlderTasks();

            if (string.IsNullOrWhiteSpace(Response))
            {
                Console.WriteLine("CompareClientPinnedAppsToServer: empty response.");
                return;
            }

            try
            {
                var payload = Newtonsoft.Json.Linq.JObject.Parse(Response);

                // Extract GUID case-insensitively
                string guid = payload.Value<string>("guid") ?? payload.Value<string>("GUID") ?? payload.Value<string>("Guid");

                // Locate apps array case-insitively
                var appsToken = payload["appsInfo"] ?? payload["AppsInfo"];
                if (appsToken == null || appsToken.Type != Newtonsoft.Json.Linq.JTokenType.Array)
                {
                    Console.WriteLine("No appsInfo array found in payload. Cannot determine pinned apps.");
                    return;
                }

                // parse pinned into int 0/1
                static int ParsePinnedToken(Newtonsoft.Json.Linq.JToken token)
                {
                    if (token == null) return 0;

                    try
                    {
                        return token.Type switch
                        {
                            Newtonsoft.Json.Linq.JTokenType.Boolean => token.Value<bool>() ? 1 : 0,
                            Newtonsoft.Json.Linq.JTokenType.Integer => token.Value<int>() != 0 ? 1 : 0,
                            _ => ParseFromString(token.ToString())
                        };
                    }
                    catch
                    {
                        return 0;
                    }

                    static int ParseFromString(string s)
                    {
                        s = s?.Trim() ?? "";
                        if (int.TryParse(s, out var iv)) return iv != 0 ? 1 : 0;
                        if (bool.TryParse(s, out var bv)) return bv ? 1 : 0;
                        return 0;
                    }
                }

                // convert DB object to int 0/1
                static int ToIntPinned(object o)
                {
                    if (o == null || o == DBNull.Value) return 0;
                    return o switch
                    {
                        bool b => b ? 1 : 0,
                        byte by => by != 0 ? 1 : 0,
                        short s => s != 0 ? 1 : 0,
                        int i => i != 0 ? 1 : 0,
                        long l => l != 0 ? 1 : 0,
                        string str when int.TryParse(str, out var iv) => iv != 0 ? 1 : 0,
                        string str when bool.TryParse(str, out var bv) => bv ? 1 : 0,
                        _ => int.TryParse(o.ToString(), out var rv) ? (rv != 0 ? 1 : 0) : 0
                    };
                }

                var reported = new System.Collections.Generic.List<(string AppId, int Pinned)>();
                foreach (var item in appsToken.Children())
                {
                    if (item.Type != Newtonsoft.Json.Linq.JTokenType.Object) continue;
                    var obj = (Newtonsoft.Json.Linq.JObject)item;
                    string appId = obj.Value<string>("id") ?? obj.Value<string>("Id") ?? obj.Value<string>("appId");
                    if (string.IsNullOrEmpty(appId)) continue;

                    var pinnedToken = obj["Pinned"] ?? obj["pinned"];
                    int pinnedInt = ParsePinnedToken(pinnedToken);
                    reported.Add((appId, pinnedInt));
                }

                if (reported.Count == 0)
                {
                    Console.WriteLine("No pinned apps reported inside appsInfo.");
                    return;
                }

                if (string.IsNullOrEmpty(guid))
                {
                    Console.WriteLine("No GUID available for laptop. Cannot create tasks.");
                    return;
                }

                // Get laptop_id
                cmd.CommandText = "SELECT id FROM Laptops WHERE GUID = @guid";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@guid", guid);
                object laptopIdObj = cmd.ExecuteScalar();
                if (laptopIdObj == null || laptopIdObj == DBNull.Value)
                {
                    Console.WriteLine("Laptop not found for GUID: " + guid);
                    return;
                }
                int laptopId = Convert.ToInt32(laptopIdObj);

                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        cmd.Transaction = transaction;

                        foreach (var (AppId, PinnedInt) in reported)
                        {
                            // Check existing app row for this laptop
                            cmd.CommandText = "SELECT Pinned FROM Apps WHERE laptop_id = @LaptopId AND app_id = @AppId";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                            cmd.Parameters.AddWithValue("@AppId", AppId);

                            object isPinnedObj = null;
                            try
                            {
                                isPinnedObj = cmd.ExecuteScalar();
                            }
                            catch
                            {
                                // treat as missing row if DB column doesn't exist or other error
                                isPinnedObj = null;
                            }

                            bool needAction = false;
                            string action = string.Empty;

                            if (isPinnedObj == null || isPinnedObj == DBNull.Value)
                            {
                                // No server record - if client reports pinned -> create pin
                                if (PinnedInt == 1)
                                {
                                    needAction = true;
                                    action = "pinned";
                                }
                            }
                            else
                            {
                                int serverPinnedInt = ToIntPinned(isPinnedObj);
                                if (serverPinnedInt != PinnedInt)
                                {
                                    needAction = true;
                                    // If client reports pinned => ensure pinned; else ensure unpinned
                                    action = PinnedInt == 1 ? "unpinned" : "pinned";
                                }
                            }

                            if (!needAction)
                            {
                                Console.WriteLine($"No action needed for AppId {AppId} on laptop {laptopId}.");
                                continue;
                            }

                            // Build winget pin command
                            string wingetCmd = action == "pinned"
                                ? $"winget pin add \"{AppId}\""
                                : $"winget pin remove \"{AppId}\"";

                            // Check for existing pending identical task to avoid duplicates
                            cmd.CommandText = @"SELECT COUNT(*) FROM Tasks WHERE laptop_id = @LaptopId AND task = @Task AND status = 1";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                            cmd.Parameters.AddWithValue("@Task", wingetCmd);
                            object cntObj = cmd.ExecuteScalar();
                            int existing = cntObj != null && cntObj != DBNull.Value ? Convert.ToInt32(cntObj) : 0;
                            if (existing > 0)
                            {
                                Console.WriteLine($"Pending task already exists for {AppId} on laptop {laptopId}: {wingetCmd}");
                                continue;
                            }

                            // Insert task
                            cmd.CommandText = @"INSERT INTO Tasks (name ,laptop_id, task, isPSCommand, status, creationDate) VALUES (@Name,@LaptopId, @Task, @IsPSCommand, @Status, @CreationDate)";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("Pin Status update", $"Winget {action} for {AppId}");
                            cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                            cmd.Parameters.AddWithValue("@Task", wingetCmd);
                            cmd.Parameters.AddWithValue("@IsPSCommand", 1);
                            cmd.Parameters.AddWithValue("@Status", 1); // pending
                            cmd.Parameters.AddWithValue("@CreationDate", DateTime.Now);
                            cmd.ExecuteNonQuery();

                            Console.WriteLine($"Enqueued winget {action} task for AppId {AppId} on laptop {laptopId}.");
                        }

                        transaction.Commit();
                    }
                    catch (Exception txEx)
                    {
                        try { transaction.Rollback(); } catch { }
                        Console.WriteLine("Error comparing pinned apps and creating tasks: " + txEx.Message);
                    }
                    finally
                    {
                        cmd.Transaction = null;
                    }
                }
            }
            catch (Newtonsoft.Json.JsonReaderException jex)
            {
                Console.WriteLine("Invalid JSON in CompareClientPinnedAppsToServer: " + jex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error in CompareClientPinnedAppsToServer: " + ex.Message);
            }
        }

        public void UpdatetaskBasedOnResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                Console.WriteLine("Response is null or empty.");
                return;
            }

            try
            {
                // Helpers
                static int? TryParseInt(object? o)
                {
                    if (o == null) return null;
                    if (o is int i) return i;
                    if (o is long l) return (int)l;
                    if (int.TryParse(o.ToString(), out var v)) return v;
                    return null;
                }

                static int MapStatusStringToValue(string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) return -1;
                    s = s.Trim().ToLowerInvariant();
                    return s switch
                    {
                        "2" => 2,
                        "3" => 3,
                        "success" => 2,
                        "ok" => 2,
                        "completed" => 2,
                        "completedsuccess" => 2,
                        "completed_success" => 2,
                        "failure" => 3,
                        "failed" => 3,
                        "error" => 3,
                        _ => -1
                    };
                }

                string idStr = null;
                string resultText = null;
                string clientResponse = null;
                int? parsedStatus = null;

                // 1) Try JSON
                bool parsed = false;
                try
                {
                    var jobj = Newtonsoft.Json.Linq.JObject.Parse(response);
                    // id
                    idStr = jobj.Value<string>("id") ?? jobj.Value<string>("ID") ?? jobj.Value<string>("Id") ?? jobj.Value<string>("taskId");
                    // result/message
                    resultText = jobj.Value<string>("result") ?? jobj.Value<string>("Result") ?? jobj.Value<string>("message") ?? jobj.Value<string>("response");
                    // clientResponse explicit
                    clientResponse = jobj.Value<string>("clientResponse") ?? jobj.Value<string>("client_response") ?? jobj.Value<string>("ClientResponse");
                    // status
                    var statusToken = jobj["status"] ?? jobj["Status"];
                    if (statusToken != null)
                    {
                        if (statusToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                            parsedStatus = statusToken.Value<int>();
                        else
                        {
                            var sts = statusToken.ToString();
                            if (int.TryParse(sts, out var si)) parsedStatus = si;
                            else
                            {
                                var mapped = MapStatusStringToValue(sts);
                                if (mapped > 0) parsedStatus = mapped;
                            }
                        }
                    }

                    parsed = true;
                }
                catch (Newtonsoft.Json.JsonReaderException)
                {
                    // not JSON, will try other formats
                    parsed = false;
                }

                // 2) If not JSON, try key=value pairs (semicolon or comma separated), or colon pairs
                if (!parsed)
                {
                    // Split on ';' or ',' first
                    var pairs = response.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pairs.Length <= 1)
                    {
                        // fallback to colon-split legacy or single-pair
                        pairs = response.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                    }

                    if (pairs.Length > 0)
                    {
                        foreach (var raw in pairs)
                        {
                            var part = raw.Trim();
                            string key = null;
                            string val = null;

                            // Try key=value or key: value formats
                            int idx = part.IndexOf('=');
                            if (idx >= 0)
                            {
                                key = part.Substring(0, idx).Trim();
                                val = part.Substring(idx + 1).Trim();
                            }
                            else
                            {
                                idx = part.IndexOf(':');
                                if (idx >= 0)
                                {
                                    key = part.Substring(0, idx).Trim();
                                    val = part.Substring(idx + 1).Trim();
                                }
                                else
                                {
                                    // If only one part and contains no key, treat as legacy "ID:Result[:ClientResponse]" below
                                    key = null;
                                    val = part;
                                }
                            }

                            if (key == null)
                                continue;

                            var lk = key.ToLowerInvariant();
                            if (lk == "id" || lk == "taskid" || lk == "tid")
                                idStr = val;
                            else if (lk == "result" || lk == "message" || lk == "response" || lk == "resulttext")
                                resultText = val;
                            else if (lk == "clientresponse" || lk == "client_response")
                                clientResponse = val;
                            else if (lk == "status")
                            {
                                if (int.TryParse(val, out var si)) parsedStatus = si;
                                else
                                {
                                    var mapped = MapStatusStringToValue(val);
                                    if (mapped > 0) parsedStatus = mapped;
                                }
                            }
                        }
                    }
                }

                // 3) Legacy fallback: "ID:Result[:ClientResponse]"
                if (string.IsNullOrWhiteSpace(idStr) && response.Contains(":"))
                {
                    var parts = response.Split(new[] { ':' }, 3);
                    if (parts.Length >= 2)
                    {
                        idStr = parts[0].Trim();
                        resultText ??= parts[1].Trim();
                        if (parts.Length >= 3)
                            clientResponse ??= parts[2].Trim();
                    }
                }

                // Validate id
                if (string.IsNullOrWhiteSpace(idStr) || !int.TryParse(idStr, out int taskId))
                {
                    Console.WriteLine("Unable to parse task ID from response. Raw response: " + response);
                    return;
                }

                // Determine statusValue
                int statusValue;
                if (parsedStatus.HasValue)
                {
                    statusValue = parsedStatus.Value;
                }
                else
                {
                    // Map resultText if present
                    if (!string.IsNullOrWhiteSpace(resultText))
                    {
                        var mapped = MapStatusStringToValue(resultText);
                        statusValue = mapped > 0 ? mapped : 3;
                    }
                    else
                    {
                        // Default to failure if unknown
                        statusValue = 3;
                    }
                }

                // Determine clientResponse to store
                string clientRespToStore = clientResponse ?? resultText ?? "";

                // Update DB
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"UPDATE Tasks 
                                            SET status = @Status,
                                                clientResponse = @ClientResponse
                                            WHERE id = @ID";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@Status", statusValue);
                        cmd.Parameters.AddWithValue("@ClientResponse", string.IsNullOrEmpty(clientRespToStore) ? (object)DBNull.Value : (object)clientRespToStore);
                        cmd.Parameters.AddWithValue("@ID", taskId);

                        int affected = cmd.ExecuteNonQuery();
                        transaction.Commit();

                        if (affected > 0)
                        {
                            Console.WriteLine($"Task {taskId} updated to status {statusValue} (response stored).");
                        }
                        else
                        {
                            Console.WriteLine($"No task found with id {taskId}. No rows affected.");
                        }
                    }
                    catch (Exception txEx)
                    {
                        try { transaction.Rollback(); } catch { }
                        Console.WriteLine("Error updating task: " + txEx.Message);
                    }
                    finally
                    {
                        cmd.Transaction = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error in UpdatetaskBasedOnResponse: " + ex.Message);
            }
        }


        public string GetUserGUIDbyHWID(string hwid)
        {
            cmd.CommandText = "SELECT GUID FROM Laptops WHERE HWID = @hwid";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@hwid", hwid);
            object laptopIdObj = cmd.ExecuteScalar();
            string laptopGuid = laptopIdObj != null && laptopIdObj != DBNull.Value ? Convert.ToString(laptopIdObj) : "";
            if (laptopGuid != "")
            {
                return laptopGuid;
            }
            else
            {
                return "No laptop found with the provided HWID.";
            }
        }

        public string UserLastTimeUpdate(string userId)
        {
            Random rnd = new Random();
            cmd.CommandText = "SELECT lastUpdate FROM Laptops where GUID = @guid";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@guid", userId);
            object result = cmd.ExecuteScalar();
            DateTime lastUpdateTime = Convert.ToDateTime(result);
            if (DateTime.Now > lastUpdateTime.AddMinutes(20 + rnd.Next(1, 6)))
            {
                return "trueface";
            }
            else
            {
                return "falseface";
            }
        }

        public string GetUserTasks(string userId)
        {
            Random rnd = new Random();
            cmd.CommandText = "SELECT lastUpdate FROM Laptops where GUID = @guid";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@guid", userId);
            object result = cmd.ExecuteScalar();
            DateTime lastUpdateTime = Convert.ToDateTime(result);
            if (DateTime.Now > lastUpdateTime.AddMinutes(20 + rnd.Next(1, 6)))
            {
                return "trueface";
            }
            else
            {
                cmd.CommandText = "SELECT Tasks.id, Tasks.task, Tasks.isPSCommand FROM Tasks LEFT JOIN Laptops ON Tasks.laptop_id = Laptops.id WHERE Laptops.GUID = @guid and Tasks.Status = 1";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@guid", userId);
                using (var reader = cmd.ExecuteReader())
                {
                    // If reader has no rows
                    if (!reader.HasRows)
                    {
                        return "falseface";
                    }

                    var rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
                    while (reader.Read())
                    {
                        var row = new System.Collections.Generic.Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            object val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            row[reader.GetName(i)] = val;
                        }
                        rows.Add(row);
                    }

                    string json = JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented);

                    return json;
                }
            }
        }

        public string UpdateUserLastCheckIn(string userId)
        {
            cmd.CommandText = "UPDATE Laptops SET lastUpdate = @lastCheckIn WHERE GUID = @guid";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@lastCheckIn", DateTime.Now);
            cmd.Parameters.AddWithValue("@guid", userId);
            int affectedRows = cmd.ExecuteNonQuery();
            if (affectedRows > 0)
            {
                return "User last check-in time updated successfully.";
            }
            else
            {
                return "No laptop found with the provided GUID.";
            }
        }

        public void InsertOrUpdateDatabase(DevicesData data)
        {
            using (var transaction = conn.BeginTransaction())
            {
                try
                {
                    cmd.Transaction = transaction;

                    // Check if laptop exists by HWID
                    cmd.CommandText = "SELECT id FROM Laptops WHERE HWID = @hwid";
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddWithValue("@hwid", data.hwid ?? (object)DBNull.Value);
                    object laptopIdObj = cmd.ExecuteScalar();
                    int laptopId = laptopIdObj != null && laptopIdObj != DBNull.Value ? Convert.ToInt32(laptopIdObj) : -1;

                    var sqlMinDate = new DateTime(1753, 1, 1);

                    string resolvedGuid = null;

                    if (laptopId > 0)
                    {
                        // Existing laptop: retrieve current GUID
                        cmd.CommandText = "SELECT GUID FROM Laptops WHERE id = @id";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@id", laptopId);
                        object existingGuidObj = cmd.ExecuteScalar();
                        string existingGuid = existingGuidObj != null && existingGuidObj != DBNull.Value ? Convert.ToString(existingGuidObj) : null;

                        // Decide which GUID to use
                        if (!string.IsNullOrWhiteSpace(data.guid) && !string.Equals(data.guid, existingGuid, StringComparison.OrdinalIgnoreCase))
                        {
                            // Check if incoming GUID is already used by another laptop
                            cmd.CommandText = "SELECT id FROM Laptops WHERE GUID = @guid";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@guid", data.guid);
                            object otherIdObj = cmd.ExecuteScalar();
                            if (otherIdObj != null && otherIdObj != DBNull.Value && Convert.ToInt32(otherIdObj) != laptopId)
                            {
                                // Conflict: incoming GUID belongs to another device -> keep existing GUID
                                Console.WriteLine("Incoming GUID already assigned to another device. Keeping existing GUID for this laptop.");
                                resolvedGuid = existingGuid;
                            }
                            else
                            {
                                // Safe to adopt incoming GUID
                                resolvedGuid = data.guid;
                            }
                        }
                        else
                        {
                            // Keep existing GUID (or null if none)
                            resolvedGuid = existingGuid;
                        }

                        // Update laptop record (ensure GUID column is updated to resolvedGuid)
                        cmd.CommandText = @"UPDATE Laptops 
                    SET name = @name,
                        OS = @os,
                        OSVer = @osVer,
                        lastUpdate = @lastUpdate,
                        MAC = @Mac,
                        HWID = @hwid,
                        GUID = @guid
                    WHERE HWID = @hwid";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@name", data.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@os", data.Os ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@osVer", data.OsVer ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@lastUpdate",
                            data.LastUpdate < sqlMinDate ? (object)DBNull.Value : data.LastUpdate);
                        cmd.Parameters.AddWithValue("@Mac", data.mac);
                        cmd.Parameters.AddWithValue("@hwid", data.hwid);
                        cmd.Parameters.AddWithValue("@guid", string.IsNullOrWhiteSpace(resolvedGuid) ? (object)DBNull.Value : resolvedGuid);
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("Existing record updated.");
                    }
                    else
                    {
                        // New laptop: determine a unique GUID to insert
                        string guidToInsert = null;
                        if (!string.IsNullOrWhiteSpace(data.guid))
                        {
                            // If provided, check if it's already used
                            cmd.CommandText = "SELECT id FROM Laptops WHERE GUID = @guid";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@guid", data.guid);
                            object existingObj = cmd.ExecuteScalar();
                            if (existingObj != null && existingObj != DBNull.Value)
                            {
                                // Provided GUID already exists; generate a new unique GUID
                                Console.WriteLine("Provided GUID already exists in database. Generating a new unique GUID for the new laptop.");
                                do
                                {
                                    guidToInsert = Guid.NewGuid().ToString();
                                    cmd.CommandText = "SELECT COUNT(1) FROM Laptops WHERE GUID = @newguid";
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@newguid", guidToInsert);
                                    int cnt = Convert.ToInt32(cmd.ExecuteScalar());
                                    if (cnt == 0) break;
                                } while (true);
                            }
                            else
                            {
                                // Provided GUID is unique -> use it
                                guidToInsert = data.guid;
                            }
                        }
                        else
                        {
                            // No GUID provided -> generate a unique one
                            do
                            {
                                guidToInsert = Guid.NewGuid().ToString();
                                cmd.CommandText = "SELECT COUNT(1) FROM Laptops WHERE GUID = @newguid";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@newguid", guidToInsert);
                                int cnt = Convert.ToInt32(cmd.ExecuteScalar());
                                if (cnt == 0) break;
                            } while (true);
                        }

                        // Insert laptop with resolved unique GUID
                        cmd.CommandText = @"INSERT INTO Laptops (name, OS, OSVer, lastUpdate, MAC, HWID, GUID) 
                    OUTPUT INSERTED.id
                    VALUES (@name, @os, @osVer, @lastUpdate, @Mac, @hwid, @guid)";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@name", data.Name ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@os", data.Os ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@osVer", data.OsVer ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@lastUpdate",
                            data.LastUpdate < sqlMinDate ? (object)DBNull.Value : data.LastUpdate);
                        cmd.Parameters.AddWithValue("@Mac", data.mac ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@hwid", data.hwid ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@guid", guidToInsert ?? (object)DBNull.Value);
                        laptopId = Convert.ToInt32(cmd.ExecuteScalar());
                        Console.WriteLine("New record inserted.");
                    }

                    // Insert/Update Apps table using laptop_id
                    foreach (var app in data.appsInfo)
                    {
                        // Check if app exists for this laptop using INNER JOIN
                        cmd.CommandText = @"
                    SELECT COUNT(*) 
                    FROM Apps 
                    INNER JOIN Laptops ON Apps.laptop_id = Laptops.id
                    WHERE Apps.laptop_id = @LaptopId AND Apps.app_id = @AppId";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                        cmd.Parameters.AddWithValue("@AppId", app.Id ?? (object)DBNull.Value);
                        int appCount = (int)cmd.ExecuteScalar();

                        if (appCount > 0)
                        {
                            // Update app
                            cmd.CommandText = @"UPDATE Apps SET 
                            Name = @Name,
                            installedVersion = @InstalledVersion, 
                            AvailableVersions = @AvailableVersions, 
                            IsUpdateAvailable = @IsUpdateAvailable
                            WHERE laptop_id = @LaptopId AND app_id = @AppId";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@Name", app.Name);
                            cmd.Parameters.AddWithValue("@InstalledVersion", app.InstalledVersion ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@AvailableVersions", app.AvailableVersions ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@IsUpdateAvailable", app.IsUpdateAvailable);
                            cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                            cmd.Parameters.AddWithValue("@AppId", app.Id ?? (object)DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            // Insert app
                            cmd.CommandText = @"INSERT INTO Apps (name,app_id, installedVersion, AvailableVersions, IsUpdateAvailable, laptop_id)
                            VALUES (@Name ,@AppId, @InstalledVersion, @AvailableVersions, @IsUpdateAvailable, @LaptopId)";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@Name", app.Name ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@AppId", app.Id ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@InstalledVersion", app.InstalledVersion ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@AvailableVersions", app.AvailableVersions ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@IsUpdateAvailable", app.IsUpdateAvailable);
                            cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // Insert Winlogs table using laptop_id
                    if (data.winGetLog != null)
                    {
                        foreach (var log in data.winGetLog)
                        {
                            cmd.CommandText = @"INSERT INTO Winlogs (laptop_id, Logs, time) VALUES (@LaptopId, @Logs, @Time)";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@LaptopId", laptopId);
                            cmd.Parameters.AddWithValue("@Logs", log ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Time", DateTime.Now);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    Console.WriteLine("Database error: " + ex.Message);
                }
                finally
                {
                    cmd.Transaction = null;
                }
            }
        }


        public string EncryptString(string plainText, string key)
        {
            byte[] iv = new byte[16];
            byte[] array;
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = iv;
            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using (var memoryStream = new System.IO.MemoryStream())
            {
                using (var cryptoStream = new CryptoStream((System.IO.Stream)memoryStream, encryptor, CryptoStreamMode.Write))
                {
                    using (var streamWriter = new System.IO.StreamWriter((System.IO.Stream)cryptoStream))
                    {
                        streamWriter.Write(plainText);
                    }
                    array = memoryStream.ToArray();
                }
            }
            return Convert.ToBase64String(array);


        }

        public string DecryptString(string cipherText, string key)
        {
            byte[] iv = new byte[16];
            byte[] buffer = Convert.FromBase64String(cipherText);
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.IV = iv;
            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using (var memoryStream = new System.IO.MemoryStream(buffer))
            {
                using (var cryptoStream = new CryptoStream((System.IO.Stream)memoryStream, decryptor, CryptoStreamMode.Read))
                {
                    using (var streamReader = new System.IO.StreamReader((System.IO.Stream)cryptoStream))
                    {
                        return streamReader.ReadToEnd();
                    }
                }
            }
        }

    }


}


