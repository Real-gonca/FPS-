using HL.Optimizer.Pro.Core.Interfaces;
using HL.Optimizer.Pro.Core.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace HL.Optimizer.Pro.Core.Services;

public class LogService : ILogService
{
    private string _dbPath = "";

    public void Initialize()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HL Optimizer Pro");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "logs.db");

        try
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Logs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Result TEXT NOT NULL,
                    Details TEXT,
                    Command TEXT,
                    User TEXT,
                    Error TEXT,
                    FreedBytes INTEGER
                );
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT
                );
                CREATE TABLE IF NOT EXISTS AppliedTweaks (
                    Id TEXT PRIMARY KEY,
                    Name TEXT,
                    AppliedAt TEXT,
                    PreviousValue TEXT,
                    CurrentValue TEXT
                );
            ";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Fallback to file-based logging if SQLite fails
        }
    }

    public async Task LogAsync(string action, string category, string result, string details = "", string command = "", string? error = null, long? freedBytes = null)
    {
        await Task.Run(() =>
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO Logs (Timestamp, Action, Category, Result, Details, Command, User, Error, FreedBytes)
                    VALUES ($ts, $action, $cat, $result, $details, $cmd, $user, $err, $freed)
                ";
                cmd.Parameters.AddWithValue("$ts", DateTime.Now.ToString("o"));
                cmd.Parameters.AddWithValue("$action", action);
                cmd.Parameters.AddWithValue("$cat", category);
                cmd.Parameters.AddWithValue("$result", result);
                cmd.Parameters.AddWithValue("$details", details);
                cmd.Parameters.AddWithValue("$cmd", command);
                cmd.Parameters.AddWithValue("$user", Environment.UserName);
                cmd.Parameters.AddWithValue("$err", error ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$freed", freedBytes ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Fallback to file
                try
                {
                    var logFile = Path.Combine(Path.GetDirectoryName(_dbPath) ?? "", "hl_optimizer.log");
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {category} | {action} | {result} | {details} | {error}\n";
                    File.AppendAllText(logFile, line);
                }
                catch { }
            }
        });
    }

    public async Task<List<LogEntry>> GetLogsAsync(int limit = 100)
    {
        return await Task.Run(() =>
        {
            var list = new List<LogEntry>();
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Timestamp, Action, Category, Result, Details, Command, User, Error, FreedBytes FROM Logs ORDER BY Id DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", limit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new LogEntry
                    {
                        Id = reader.GetInt32(0),
                        Timestamp = DateTime.TryParse(reader.GetString(1), out var dt) ? dt : DateTime.Now,
                        Action = reader.GetString(2),
                        Category = reader.GetString(3),
                        Result = reader.GetString(4),
                        Details = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        Command = reader.IsDBNull(6) ? "" : reader.GetString(6),
                        User = reader.IsDBNull(7) ? "" : reader.GetString(7),
                        Error = reader.IsDBNull(8) ? null : reader.GetString(8),
                        FreedBytes = reader.IsDBNull(9) ? null : reader.GetInt64(9)
                    });
                }
            }
            catch { }
            return list;
        });
    }

    public async Task ClearLogsAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Logs";
                cmd.ExecuteNonQuery();
            }
            catch { }
        });
    }
}
