using System;
using Microsoft.Data.Sqlite;
var connStr = "Data Source=C:\\ProgramData\\Jellyfin\\Server\\plugins\\NotifySync\\notifications.db;Mode=ReadOnly";
using var conn = new SqliteConnection(connStr);
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT count(*) FROM Notifications";
var count = cmd.ExecuteScalar();
Console.WriteLine($"Row count: {count}");
