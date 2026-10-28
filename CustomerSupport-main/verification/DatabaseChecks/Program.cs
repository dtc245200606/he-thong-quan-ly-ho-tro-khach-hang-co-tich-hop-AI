using Microsoft.Data.Sqlite;

try
{
    if (args.Length == 3 && args[0] == "backup") Backup(args[1], args[2]);
    else if (args.Length == 3 && args[0] == "restore") Restore(args[1], args[2]);
    else if (args.Length == 0) SelfTest();
    else throw new ArgumentException("Usage: backup <database> <new-file> | restore <backup> <database>; no arguments runs isolated checks.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Database operation refused/failed: " + error.Message);
    return 1;
}

static SqliteConnection Open(string path, SqliteOpenMode mode)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    { DataSource = Path.GetFullPath(path), Mode = mode, Pooling = false, DefaultTimeout = 5 }.ToString());
    try { connection.Open(); return connection; }
    catch { connection.Dispose(); throw; }
}

static void Validate(SqliteConnection connection)
{
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA integrity_check";
    using var reader = command.ExecuteReader();
    if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
        throw new InvalidDataException("SQLite integrity_check did not return ok.");
}

static void Backup(string source, string destination)
{
    source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
    if (!File.Exists(source)) throw new FileNotFoundException("Source database does not exist.");
    if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Source and destination must differ.");
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    // Reserve a new file. Existing backups are never overwritten.
    using (new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
    try
    {
        using var input = Open(source, SqliteOpenMode.ReadOnly);
        Validate(input);
        using var output = Open(destination, SqliteOpenMode.ReadWrite);
        input.BackupDatabase(output);
        // Produce a standalone database, with no WAL dependency.
        using var journal = output.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode=DELETE";
        journal.ExecuteScalar();
        Validate(output);
    }
    catch { File.Delete(destination); throw; }
    Console.WriteLine("Backup verified: " + destination);
}

static void Restore(string source, string target)
{
    source = Path.GetFullPath(source); target = Path.GetFullPath(target);
    if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        throw new IOException("Backup and target must differ.");
    // These scripts target Windows. Refuse recovery files instead of discarding local data.
    if (File.Exists(target + "-wal") || File.Exists(target + "-shm") || File.Exists(target + "-journal"))
        throw new IOException("Target has SQLite recovery files. Stop the web application gracefully and let SQLite finish recovery/checkpoint first. No files were removed.");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    var staging = target + ".restore-" + Guid.NewGuid().ToString("N") + ".db";
    try
    {
        Backup(source, staging); // Validate and normalize the source before touching the target.
        var existed = File.Exists(target);
        using var database = new FileStream(target, existed ? FileMode.Open : FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        // FileShare.None prevents overwriting a database held open by the Windows web process.
        if (File.Exists(target + "-wal") || File.Exists(target + "-shm") || File.Exists(target + "-journal"))
            throw new IOException("Recovery files appeared; restore refused.");
        string? rescue = null;
        if (existed)
        {
            rescue = target + ".before-restore-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".db";
            using var safety = new FileStream(rescue, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            database.CopyTo(safety); safety.Flush(true);
        }
        try
        {
            database.Position = 0;
            using var replacement = File.OpenRead(staging);
            replacement.CopyTo(database); database.SetLength(database.Position); database.Flush(true);
        }
        catch
        {
            if (rescue is not null)
            {
                database.Position = 0;
                using var original = File.OpenRead(rescue);
                original.CopyTo(database); database.SetLength(database.Position); database.Flush(true);
            }
            throw;
        }
        Console.WriteLine("Restored: " + target);
        if (rescue is not null) Console.WriteLine("Previous database retained: " + rescue);
    }
    finally { if (File.Exists(staging)) File.Delete(staging); }
}

static void SelfTest()
{
    var directory = Path.Combine(Path.GetTempPath(), "CustomerSupportDatabaseChecks-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var database = Path.Combine(directory, "source.db");
        var backup = Path.Combine(directory, "backup.db");
        using (var live = Open(database, SqliteOpenMode.ReadWriteCreate))
        {
            using var command = live.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Checks (Id INTEGER PRIMARY KEY, Body TEXT NOT NULL); INSERT INTO Checks VALUES (1, 'original');";
            command.ExecuteNonQuery();
            Backup(database, backup);
            using var copied = Open(backup, SqliteOpenMode.ReadOnly);
            using var read = copied.CreateCommand(); read.CommandText = "SELECT Body FROM Checks";
            Check((string?)read.ExecuteScalar() == "original", "online WAL backup includes committed data");
            Refused(() => Backup(database, backup), "existing backup is not overwritten");
            Refused(() => Restore(backup, database), "live WAL database restore refused");
        }
        var target = Path.Combine(directory, "target.db");
        Restore(backup, target);
        using (var held = File.Open(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Refused(() => Restore(backup, target), "open target restore refused");
        Restore(backup, target);
        Check(Directory.GetFiles(directory, "target.db.before-restore-*.db").Length == 1, "previous database retained before replacement");
        var invalid = Path.Combine(directory, "invalid.db"); File.WriteAllText(invalid, "not SQLite");
        var before = File.ReadAllBytes(target);
        Refused(() => Restore(invalid, target), "invalid backup refused");
        Check(before.SequenceEqual(File.ReadAllBytes(target)), "failed restore preserves target bytes");
        Console.WriteLine("All database checks passed.");
    }
    finally { Directory.Delete(directory, true); }
}
static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}
static void Refused(Action action, string name)
{
    try { action(); }
    catch (Exception error) when (error is IOException or SqliteException) { Check(true, name); return; }
    throw new Exception("FAILED: " + name);
}
