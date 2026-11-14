using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Pocket.Client.Controls;
using Pocket.Client.Data;
using Pocket.Client.Models;
using SQLite;
using SQLitePCL;

namespace Pocket.Client.Services
{
    public class BackupTableColumnSchema
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "TEXT";
        public bool NotNull { get; set; }
        public string? DefaultValue { get; set; }
        public bool IsPrimaryKey { get; set; }
    }

    public class BackupTableSchema
    {
        public string TableName { get; set; } = string.Empty;
        public string? CreateTableSql { get; set; }
        public List<string> CreateIndexSqls { get; set; } = new();
        public List<BackupTableColumnSchema> Columns { get; set; } = new();
        public int RowCount { get; set; }
    }

    public class BackupManifest
    {
        public int FormatVersion { get; set; } = 1;
        public string AppName { get; set; } = "Pocket";
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
        public string Platform { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int MediaFilesCount { get; set; }
        public int ProfileFilesCount { get; set; }
        public List<BackupTableSchema> Schemas { get; set; } = new();
    }

    public class BackupFriendSecretInfo
    {
        public string FriendId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FriendPublicKey { get; set; } = string.Empty;
        public string SharedSecretBase64 { get; set; } = string.Empty;
    }

    public class BackupCryptographySection
    {
        public string Algorithm { get; set; } = "ECDH-P256 + HKDF-SHA256 (Pocket.E2EE.AES256GCM.v1)";
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
        public List<BackupFriendSecretInfo> FriendSharedSecrets { get; set; } = new();
    }

    public class BackupDataDump
    {
        public BackupManifest Manifest { get; set; } = new();
        public BackupCryptographySection Cryptography { get; set; } = new();
        public Dictionary<string, string?> Preferences { get; set; } = new();
        public Dictionary<string, List<Dictionary<string, object?>>> Tables { get; set; } = new();
    }

    public class BackupExportPackage
    {
        public string SuggestedFileName { get; set; } = string.Empty;
        public byte[] ZipBytes { get; set; } = Array.Empty<byte>();
        public BackupManifest Manifest { get; set; } = new();
    }

    public class BackupImportResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string RestoredUsername { get; set; } = string.Empty;
        public int TablesProcessed { get; set; }
        public int TotalRowsImported { get; set; }
        public int FriendsCount { get; set; }
        public int PostsCount { get; set; }
        public int MessagesAndReactionsCount { get; set; }
        public int MediaFilesRestored { get; set; }
        public int SharedSecretsCount { get; set; }
    }

    public interface IBackupService
    {
        Task<BackupExportPackage> CreateExportPackageAsync(string? currentPrivateKey = null);
        Task<BackupImportResult> ImportBackupAsync(Stream inputStream, string fileName, bool mergeWithExisting);
    }

    public class BackupService : IBackupService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly LocalDatabase _database;
        private readonly ICryptoService _cryptoService;

        public BackupService(LocalDatabase database, ICryptoService cryptoService)
        {
            _database = database;
            _cryptoService = cryptoService;
        }

        public async Task<BackupExportPackage> CreateExportPackageAsync(string? currentPrivateKey = null)
        {
            var asyncConn = await _database.GetConnectionAsync();
            var user = await _database.GetUserAsync();
            var friends = await _database.GetFriendsAsync();

            string privateKey = currentPrivateKey ?? string.Empty;
            if (string.IsNullOrEmpty(privateKey) && user != null)
            {
                try
                {
                    privateKey = await SecureStorage.Default.GetAsync($"pocket_pk_{user.Id}") ?? string.Empty;
                }
                catch
                {
                    // Fallback to DB column
                }

                if (string.IsNullOrEmpty(privateKey))
                {
                    privateKey = user.EncryptedPrivateKey ?? string.Empty;
                }
            }

            // Ensure the SQLite User record also holds the private key so the .db3 file is self-contained
            if (user != null && !string.IsNullOrEmpty(privateKey) && user.EncryptedPrivateKey != privateKey)
            {
                user.EncryptedPrivateKey = privateKey;
                await _database.SaveUserAsync(user);
            }

            // Flush WAL into the main database file before reading
            try
            {
                await asyncConn.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE);");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackupService] WAL checkpoint skipped: {ex.Message}");
            }

            var appDataDir = FileSystem.AppDataDirectory;
            var schemas = new List<BackupTableSchema>();
            var tablesData = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

            // Dynamically introspect all tables, columns, and rows using SQLitePCL.raw
            var connWithLock = asyncConn.GetConnection();
            using (connWithLock.Lock())
            {
                var db = connWithLock.Handle;
                var tableEntries = ReadAllUserTables(db);
                var indexEntries = ReadAllUserIndexes(db);

                foreach (var (tableName, createSql) in tableEntries)
                {
                    var columns = ReadTableColumns(db, tableName);
                    var rows = ReadTableRowsNormalized(db, tableName, appDataDir);

                    var schema = new BackupTableSchema
                    {
                        TableName = tableName,
                        CreateTableSql = createSql,
                        CreateIndexSqls = indexEntries.TryGetValue(tableName, out var idxList) ? idxList : new List<string>(),
                        Columns = columns,
                        RowCount = rows.Count
                    };

                    schemas.Add(schema);
                    tablesData[tableName] = rows;
                }
            }

            // Build E2EE Cryptography section with derived shared secrets for each friend
            var cryptoSection = new BackupCryptographySection
            {
                UserId = user?.Id ?? string.Empty,
                Username = user?.Username ?? string.Empty,
                PublicKey = user?.PublicKey ?? string.Empty,
                PrivateKey = privateKey
            };

            if (!string.IsNullOrEmpty(privateKey))
            {
                foreach (var friend in friends)
                {
                    string sharedSecretBase64 = string.Empty;
                    if (!string.IsNullOrEmpty(friend.PublicKey))
                    {
                        try
                        {
                            var secretBytes = _cryptoService.DeriveSharedSecret(privateKey, friend.PublicKey);
                            sharedSecretBase64 = Convert.ToBase64String(secretBytes);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[BackupService] Could not derive secret for {friend.Username}: {ex.Message}");
                        }
                    }

                    cryptoSection.FriendSharedSecrets.Add(new BackupFriendSecretInfo
                    {
                        FriendId = friend.Id,
                        Username = friend.Username,
                        DisplayName = friend.DisplayName,
                        FriendPublicKey = friend.PublicKey,
                        SharedSecretBase64 = sharedSecretBase64
                    });
                }
            }

            // Collect Media and Profile files
            var mediaDir = Path.Combine(appDataDir, "Media");
            var profileDir = Path.Combine(appDataDir, "Profile");
            var mediaFiles = Directory.Exists(mediaDir) ? Directory.GetFiles(mediaDir) : Array.Empty<string>();
            var profileFiles = Directory.Exists(profileDir) ? Directory.GetFiles(profileDir) : Array.Empty<string>();

            var manifest = new BackupManifest
            {
                FormatVersion = 1,
                AppName = "Pocket",
                ExportedAtUtc = DateTime.UtcNow,
                Platform = Microsoft.Maui.Devices.DeviceInfo.Platform.ToString(),
                UserId = user?.Id ?? string.Empty,
                Username = user?.Username ?? "user",
                DisplayName = user?.DisplayName ?? string.Empty,
                MediaFilesCount = mediaFiles.Length,
                ProfileFilesCount = profileFiles.Length,
                Schemas = schemas
            };

            var dump = new BackupDataDump
            {
                Manifest = manifest,
                Cryptography = cryptoSection,
                Preferences = ExportPreferences(),
                Tables = tablesData
            };

            // Create a portable SQLite snapshot (PocketLocal.db3) with relativized file paths
            var tempDbPath = Path.Combine(FileSystem.CacheDirectory, $"export_{Guid.NewGuid():N}.db3");
            try
            {
                if (File.Exists(DatabaseConstants.DatabasePath))
                {
                    File.Copy(DatabaseConstants.DatabasePath, tempDbPath, overwrite: true);
                    NormalizeDatabasePathsInFile(tempDbPath, appDataDir, toRelative: true);
                }

                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    // 1. manifest.json
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    await using (var s = manifestEntry.Open())
                    {
                        await JsonSerializer.SerializeAsync(s, manifest, JsonOptions);
                    }

                    // 2. data_dump.json (human-readable & editable for experimental development)
                    var dumpEntry = archive.CreateEntry("data_dump.json", CompressionLevel.Optimal);
                    await using (var s = dumpEntry.Open())
                    {
                        await JsonSerializer.SerializeAsync(s, dump, JsonOptions);
                    }

                    // 3. PocketLocal.db3 (portable SQLite database file)
                    if (File.Exists(tempDbPath))
                    {
                        var dbEntry = archive.CreateEntry("PocketLocal.db3", CompressionLevel.Optimal);
                        await using var dbEntryStream = dbEntry.Open();
                        await using var tempDbStream = File.OpenRead(tempDbPath);
                        await tempDbStream.CopyToAsync(dbEntryStream);
                    }

                    // 4. Media/* files
                    foreach (var file in mediaFiles)
                    {
                        var entryName = $"Media/{Path.GetFileName(file)}";
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await using var fileStream = File.OpenRead(file);
                        await fileStream.CopyToAsync(entryStream);
                    }

                    // 5. Profile/* files
                    foreach (var file in profileFiles)
                    {
                        var entryName = $"Profile/{Path.GetFileName(file)}";
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        await using var entryStream = entry.Open();
                        await using var fileStream = File.OpenRead(file);
                        await fileStream.CopyToAsync(entryStream);
                    }
                }

                string safeUser = SanitizeFileName(string.IsNullOrWhiteSpace(user?.Username) ? "user" : user.Username);
                string suggestedFileName = $"pocket_backup_{safeUser}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

                return new BackupExportPackage
                {
                    SuggestedFileName = suggestedFileName,
                    ZipBytes = memoryStream.ToArray(),
                    Manifest = manifest
                };
            }
            finally
            {
                TryDeleteFile(tempDbPath);
            }
        }

        public async Task<BackupImportResult> ImportBackupAsync(Stream inputStream, string fileName, bool mergeWithExisting)
        {
            var result = new BackupImportResult();
            var tempInputPath = Path.Combine(FileSystem.CacheDirectory, $"import_in_{Guid.NewGuid():N}");
            var tempExtractDir = Path.Combine(FileSystem.CacheDirectory, $"import_ext_{Guid.NewGuid():N}");

            try
            {
                await using (var tempOut = File.Create(tempInputPath))
                {
                    await inputStream.CopyToAsync(tempOut);
                }

                var header = new byte[16];
                int readBytes;
                await using (var headerStream = File.OpenRead(tempInputPath))
                {
                    readBytes = await headerStream.ReadAsync(header, 0, header.Length);
                }

                bool isZip = readBytes >= 4 && header[0] == 'P' && header[1] == 'K' && header[2] == 3 && header[3] == 4;
                bool isSqlite = readBytes >= 15 && Encoding.ASCII.GetString(header, 0, 15).StartsWith("SQLite format 3", StringComparison.Ordinal);

                var appDataDir = FileSystem.AppDataDirectory;
                var mediaDir = Path.Combine(appDataDir, "Media");
                var profileDir = Path.Combine(appDataDir, "Profile");
                Directory.CreateDirectory(mediaDir);
                Directory.CreateDirectory(profileDir);

                string? extractedDbPath = null;
                BackupDataDump? dataDump = null;

                if (isZip)
                {
                    Directory.CreateDirectory(tempExtractDir);
                    ZipFile.ExtractToDirectory(tempInputPath, tempExtractDir, overwriteFiles: true);

                    if (!mergeWithExisting)
                    {
                        ClearDirectoryFiles(mediaDir);
                        ClearDirectoryFiles(profileDir);
                    }

                    // Copy Media files from ZIP
                    var zipMediaDir = Path.Combine(tempExtractDir, "Media");
                    if (Directory.Exists(zipMediaDir))
                    {
                        foreach (var srcFile in Directory.GetFiles(zipMediaDir))
                        {
                            var destFile = Path.Combine(mediaDir, Path.GetFileName(srcFile));
                            File.Copy(srcFile, destFile, overwrite: true);
                            result.MediaFilesRestored++;
                        }
                    }

                    // Copy Profile files from ZIP
                    var zipProfileDir = Path.Combine(tempExtractDir, "Profile");
                    if (Directory.Exists(zipProfileDir))
                    {
                        foreach (var srcFile in Directory.GetFiles(zipProfileDir))
                        {
                            var destFile = Path.Combine(profileDir, Path.GetFileName(srcFile));
                            File.Copy(srcFile, destFile, overwrite: true);
                            result.MediaFilesRestored++;
                        }
                    }

                    var candidateDb = Path.Combine(tempExtractDir, "PocketLocal.db3");
                    if (File.Exists(candidateDb))
                    {
                        extractedDbPath = candidateDb;
                    }
                    else
                    {
                        var anyDb = Directory.GetFiles(tempExtractDir, "*.db3", SearchOption.AllDirectories).FirstOrDefault();
                        if (anyDb != null)
                            extractedDbPath = anyDb;
                    }

                    var candidateJson = Path.Combine(tempExtractDir, "data_dump.json");
                    if (File.Exists(candidateJson))
                    {
                        await using var jsonStream = File.OpenRead(candidateJson);
                        dataDump = await JsonSerializer.DeserializeAsync<BackupDataDump>(jsonStream, JsonOptions);
                    }
                }
                else if (isSqlite || fileName.EndsWith(".db3", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    extractedDbPath = tempInputPath;
                }
                else
                {
                    // Standalone JSON dump (data_dump.json)
                    await using var jsonStream = File.OpenRead(tempInputPath);
                    dataDump = await JsonSerializer.DeserializeAsync<BackupDataDump>(jsonStream, JsonOptions);
                }

                if (extractedDbPath == null && dataDump == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Seçilen dosya geçerli bir Pocket yedeği (.zip, .json veya .db3) değil.";
                    return result;
                }

                // Ensure the local database is initialized with current C# model tables & columns
                await _database.ReinitializeAsync();
                var asyncConn = await _database.GetConnectionAsync();

                var connWithLock = asyncConn.GetConnection();
                using (connWithLock.Lock())
                {
                    var db = connWithLock.Handle;
                    ExecuteNonQuery(db, "BEGIN IMMEDIATE TRANSACTION;");
                    try
                    {
                        if (!mergeWithExisting)
                        {
                            // Clean Restore: clear existing rows in all user tables while keeping the schema
                            var existingTables = ReadAllUserTables(db);
                            foreach (var (tName, _) in existingTables)
                            {
                                ExecuteNonQuery(db, $"DELETE FROM \"{EscapeIdentifier(tName)}\";");
                            }
                        }

                        // Step 1: If a SQLite .db3 file is provided, evolve schema & import rows from it
                        if (!string.IsNullOrEmpty(extractedDbPath) && File.Exists(extractedDbPath))
                        {
                            var statsFromDb = ImportFromSqliteFile(db, extractedDbPath, appDataDir);
                            result.TablesProcessed = Math.Max(result.TablesProcessed, statsFromDb.TablesCount);
                            result.TotalRowsImported = Math.Max(result.TotalRowsImported, statsFromDb.RowsCount);
                        }

                        // Step 2: If data_dump.json is provided, evolve schema & upsert rows from it
                        // (This ensures any experimental edits made in data_dump.json or standalone JSON take precedence)
                        if (dataDump?.Tables is { Count: > 0 })
                        {
                            var statsFromJson = ImportFromDataDump(db, dataDump, appDataDir, mergeWithExisting);
                            result.TablesProcessed = Math.Max(result.TablesProcessed, statsFromJson.TablesCount);
                            result.TotalRowsImported = Math.Max(result.TotalRowsImported, statsFromJson.RowsCount);
                        }

                        ExecuteNonQuery(db, "COMMIT;");
                    }
                    catch
                    {
                        try { ExecuteNonQuery(db, "ROLLBACK;"); } catch { }
                        throw;
                    }
                }

                // Restore Preferences if present in data_dump.json
                if (dataDump?.Preferences is { Count: > 0 })
                {
                    ImportPreferences(dataDump.Preferences);
                }

                // Restore PrivateKey into SecureStorage & User table
                var restoredUser = await _database.GetUserAsync();
                if (restoredUser != null)
                {
                    string restoredPrivateKey = dataDump?.Cryptography?.PrivateKey ?? string.Empty;
                    if (string.IsNullOrEmpty(restoredPrivateKey))
                    {
                        restoredPrivateKey = restoredUser.EncryptedPrivateKey ?? string.Empty;
                    }

                    if (!string.IsNullOrEmpty(restoredPrivateKey))
                    {
                        if (restoredUser.EncryptedPrivateKey != restoredPrivateKey)
                        {
                            restoredUser.EncryptedPrivateKey = restoredPrivateKey;
                            await _database.SaveUserAsync(restoredUser);
                        }

                        try
                        {
                            await SecureStorage.Default.SetAsync($"pocket_pk_{restoredUser.Id}", restoredPrivateKey);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[BackupService] SecureStorage restore fallback: {ex.Message}");
                        }
                    }

                    result.RestoredUsername = restoredUser.DisplayName ?? restoredUser.Username;
                }

                var allFriends = await _database.GetFriendsAsync();
                var allPosts = await _database.GetAllMediaPostsAsync();
                result.FriendsCount = allFriends.Count;
                result.PostsCount = allPosts.Count;
                result.SharedSecretsCount = allFriends.Count(f => !string.IsNullOrEmpty(f.PublicKey));

                // Count total DirectMessages (messages + reactions)
                int dmCount = 0;
                foreach (var friend in allFriends)
                {
                    var dms = await _database.GetDirectMessagesForFriendAsync(friend.Id);
                    dmCount += dms.Count;
                }
                result.MessagesAndReactionsCount = dmCount;
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackupService] Import failed: {ex}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
            finally
            {
                TryDeleteFile(tempInputPath);
                TryDeleteDirectory(tempExtractDir);
            }
        }

        #region Dynamic SQLite Schema Evolution & Row Operations

        private static (int TablesCount, int RowsCount) ImportFromSqliteFile(sqlite3 targetDb, string sourceDbPath, string appDataDir)
        {
            int tablesCount = 0;
            int rowsCount = 0;

            int rc = raw.sqlite3_open_v2(sourceDbPath, out var srcDb, raw.SQLITE_OPEN_READONLY, null);
            if (rc != raw.SQLITE_OK)
            {
                srcDb.Dispose();
                throw new InvalidOperationException($"Yedek SQLite dosyası açılamadı (Kod: {rc}).");
            }

            try
            {
                var srcTables = ReadAllUserTables(srcDb);
                var srcIndexes = ReadAllUserIndexes(srcDb);
                var targetTables = ReadAllUserTables(targetDb).ToDictionary(t => t.TableName, t => t.CreateSql, StringComparer.OrdinalIgnoreCase);

                foreach (var (tableName, createSql) in srcTables)
                {
                    // If the backup has a table that doesn't exist in target DB, create it dynamically
                    if (!targetTables.ContainsKey(tableName) && !string.IsNullOrWhiteSpace(createSql))
                    {
                        string safeCreate = MakeCreateTableIfNotExists(createSql);
                        ExecuteNonQuery(targetDb, safeCreate);
                    }

                    // Evolve columns: if source table has columns not present in target table, ADD COLUMN dynamically
                    var srcCols = ReadTableColumns(srcDb, tableName);
                    var targetColSet = ReadTableColumns(targetDb, tableName)
                        .Select(c => c.Name)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var col in srcCols)
                    {
                        if (!targetColSet.Contains(col.Name))
                        {
                            string colType = string.IsNullOrWhiteSpace(col.Type) ? "TEXT" : col.Type;
                            ExecuteNonQuery(targetDb, $"ALTER TABLE \"{EscapeIdentifier(tableName)}\" ADD COLUMN \"{EscapeIdentifier(col.Name)}\" {colType};");
                            targetColSet.Add(col.Name);
                        }
                    }

                    // Ensure indexes exist
                    if (srcIndexes.TryGetValue(tableName, out var indexSqls))
                    {
                        foreach (var idxSql in indexSqls)
                        {
                            try
                            {
                                ExecuteNonQuery(targetDb, MakeCreateIndexIfNotExists(idxSql));
                            }
                            catch
                            {
                                // Non-fatal if index already exists
                            }
                        }
                    }

                    // Read rows from source DB and upsert into target DB
                    var rows = ReadTableRowsNormalized(srcDb, tableName, appDataDir);
                    rowsCount += UpsertRowsIntoTable(targetDb, tableName, rows, appDataDir);
                    tablesCount++;
                }
            }
            finally
            {
                srcDb.Dispose();
            }

            return (tablesCount, rowsCount);
        }

        private static (int TablesCount, int RowsCount) ImportFromDataDump(
            sqlite3 targetDb,
            BackupDataDump dump,
            string appDataDir,
            bool mergeWithExisting)
        {
            int tablesCount = 0;
            int rowsCount = 0;

            var targetTables = ReadAllUserTables(targetDb)
                .ToDictionary(t => t.TableName, t => t.CreateSql, StringComparer.OrdinalIgnoreCase);

            var schemaMap = (dump.Manifest?.Schemas ?? new List<BackupTableSchema>())
                .ToDictionary(s => s.TableName, s => s, StringComparer.OrdinalIgnoreCase);

            foreach (var (tableName, rows) in dump.Tables)
            {
                if (string.IsNullOrWhiteSpace(tableName))
                    continue;

                // 1. Ensure table exists (either from manifest DDL or inferred dynamically from JSON keys)
                if (!targetTables.ContainsKey(tableName))
                {
                    if (schemaMap.TryGetValue(tableName, out var tableSchema) && !string.IsNullOrWhiteSpace(tableSchema.CreateTableSql))
                    {
                        ExecuteNonQuery(targetDb, MakeCreateTableIfNotExists(tableSchema.CreateTableSql));
                    }
                    else
                    {
                        var sampleKeys = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (sampleKeys.Count == 0)
                            continue;

                        string pkCol = sampleKeys.FirstOrDefault(k => string.Equals(k, "Id", StringComparison.OrdinalIgnoreCase)) ?? sampleKeys[0];
                        var colDefs = sampleKeys.Select(k =>
                            string.Equals(k, pkCol, StringComparison.OrdinalIgnoreCase)
                                ? $"\"{EscapeIdentifier(k)}\" TEXT PRIMARY KEY NOT NULL"
                                : $"\"{EscapeIdentifier(k)}\" TEXT");
                        ExecuteNonQuery(targetDb, $"CREATE TABLE IF NOT EXISTS \"{EscapeIdentifier(tableName)}\" ({string.Join(", ", colDefs)});");
                    }

                    targetTables[tableName] = "CREATED";
                }

                // 2. Ensure all columns in manifest schema AND JSON rows exist on the table
                var existingCols = ReadTableColumns(targetDb, tableName)
                    .ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

                if (schemaMap.TryGetValue(tableName, out var knownSchema))
                {
                    foreach (var col in knownSchema.Columns)
                    {
                        if (!existingCols.ContainsKey(col.Name))
                        {
                            string colType = string.IsNullOrWhiteSpace(col.Type) ? "TEXT" : col.Type;
                            ExecuteNonQuery(targetDb, $"ALTER TABLE \"{EscapeIdentifier(tableName)}\" ADD COLUMN \"{EscapeIdentifier(col.Name)}\" {colType};");
                            existingCols[col.Name] = col;
                        }
                    }
                }

                // Check if experimental JSON rows introduced any brand-new column not even in manifest schema
                var allJsonKeys = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var jsonKey in allJsonKeys)
                {
                    if (!existingCols.ContainsKey(jsonKey))
                    {
                        string inferredType = InferSqlTypeFromJsonRows(rows, jsonKey);
                        ExecuteNonQuery(targetDb, $"ALTER TABLE \"{EscapeIdentifier(tableName)}\" ADD COLUMN \"{EscapeIdentifier(jsonKey)}\" {inferredType};");
                        existingCols[jsonKey] = new BackupTableColumnSchema { Name = jsonKey, Type = inferredType };
                    }
                }

                // If Clean Restore and we already populated from .db3 earlier, replace with JSON rows so JSON edits win
                if (!mergeWithExisting)
                {
                    ExecuteNonQuery(targetDb, $"DELETE FROM \"{EscapeIdentifier(tableName)}\";");
                }

                rowsCount += UpsertRowsIntoTable(targetDb, tableName, rows, appDataDir);
                tablesCount++;
            }

            return (tablesCount, rowsCount);
        }

        private static int UpsertRowsIntoTable(
            sqlite3 db,
            string tableName,
            List<Dictionary<string, object?>> rows,
            string appDataDir)
        {
            if (rows == null || rows.Count == 0)
                return 0;

            var tableColumns = ReadTableColumns(db, tableName)
                .ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

            int upserted = 0;

            // User table only holds a single local identity row on clean restore
            bool isSingleUserTable = string.Equals(tableName, "User", StringComparison.OrdinalIgnoreCase);
            if (isSingleUserTable && rows.Count > 0)
            {
                ExecuteNonQuery(db, "DELETE FROM \"User\";");
                rows = new List<Dictionary<string, object?>> { rows[0] };
            }

            foreach (var row in rows)
            {
                var matchingKeys = row.Keys
                    .Where(k => tableColumns.ContainsKey(k))
                    .ToList();

                if (matchingKeys.Count == 0)
                    continue;

                var colNamesSql = string.Join(", ", matchingKeys.Select(k => $"\"{EscapeIdentifier(tableColumns[k].Name)}\""));
                var paramPlaceholders = string.Join(", ", matchingKeys.Select((_, i) => $"?{i + 1}"));
                string sql = $"INSERT OR REPLACE INTO \"{EscapeIdentifier(tableName)}\" ({colNamesSql}) VALUES ({paramPlaceholders});";

                int rc = raw.sqlite3_prepare_v2(db, sql, out var stmt);
                if (rc != raw.SQLITE_OK)
                {
                    stmt.Dispose();
                    throw new InvalidOperationException($"SQL hazırlanamadı ({tableName}): {raw.sqlite3_errmsg(db).utf8_to_string()}");
                }

                try
                {
                    for (int i = 0; i < matchingKeys.Count; i++)
                    {
                        string key = matchingKeys[i];
                        var colMeta = tableColumns[key];
                        object? rawVal = row[key];
                        BindParameterValue(stmt, i + 1, rawVal, colMeta, appDataDir);
                    }

                    int stepRc = raw.sqlite3_step(stmt);
                    if (stepRc == raw.SQLITE_DONE || stepRc == raw.SQLITE_ROW)
                    {
                        upserted++;
                    }
                }
                finally
                {
                    stmt.Dispose();
                }
            }

            return upserted;
        }

        private static void BindParameterValue(
            sqlite3_stmt stmt,
            int index,
            object? value,
            BackupTableColumnSchema colSchema,
            string appDataDir)
        {
            if (value == null)
            {
                raw.sqlite3_bind_null(stmt, index);
                return;
            }

            if (value is JsonElement je)
            {
                switch (je.ValueKind)
                {
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        raw.sqlite3_bind_null(stmt, index);
                        return;
                    case JsonValueKind.True:
                        raw.sqlite3_bind_int64(stmt, index, 1);
                        return;
                    case JsonValueKind.False:
                        raw.sqlite3_bind_int64(stmt, index, 0);
                        return;
                    case JsonValueKind.Number:
                        if (je.TryGetInt64(out long lVal))
                            raw.sqlite3_bind_int64(stmt, index, lVal);
                        else if (je.TryGetDouble(out double dVal))
                            raw.sqlite3_bind_double(stmt, index, dVal);
                        else
                            raw.sqlite3_bind_text(stmt, index, je.GetRawText());
                        return;
                    case JsonValueKind.String:
                        string strVal = je.GetString() ?? string.Empty;
                        BindStringOrDateOrPath(stmt, index, strVal, colSchema, appDataDir);
                        return;
                    default:
                        raw.sqlite3_bind_text(stmt, index, je.GetRawText());
                        return;
                }
            }

            if (value is bool b)
            {
                raw.sqlite3_bind_int64(stmt, index, b ? 1 : 0);
            }
            else if (value is long l)
            {
                raw.sqlite3_bind_int64(stmt, index, l);
            }
            else if (value is int intVal)
            {
                raw.sqlite3_bind_int64(stmt, index, intVal);
            }
            else if (value is double d)
            {
                raw.sqlite3_bind_double(stmt, index, d);
            }
            else if (value is float f)
            {
                raw.sqlite3_bind_double(stmt, index, f);
            }
            else if (value is byte[] bytes)
            {
                raw.sqlite3_bind_blob(stmt, index, bytes);
            }
            else if (value is string s)
            {
                BindStringOrDateOrPath(stmt, index, s, colSchema, appDataDir);
            }
            else
            {
                raw.sqlite3_bind_text(stmt, index, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        private static void BindStringOrDateOrPath(
            sqlite3_stmt stmt,
            int index,
            string strVal,
            BackupTableColumnSchema colSchema,
            string appDataDir)
        {
            // Support human-edited ISO-8601 dates in bigint/DateTime columns (sqlite-net-pcl stores DateTime as .NET UTC Ticks)
            if (colSchema.Type.Contains("INT", StringComparison.OrdinalIgnoreCase) ||
                colSchema.Name.EndsWith("At", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                {
                    raw.sqlite3_bind_int64(stmt, index, ticks);
                    return;
                }

                if (DateTimeOffset.TryParse(strVal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                {
                    raw.sqlite3_bind_int64(stmt, index, dto.UtcDateTime.Ticks);
                    return;
                }
            }

            string resolved = ResolvePathForCurrentDevice(strVal, appDataDir);
            raw.sqlite3_bind_text(stmt, index, resolved);
        }

        private static List<(string TableName, string CreateSql)> ReadAllUserTables(sqlite3 db)
        {
            var list = new List<(string, string)>();
            const string sql = "SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'android_%' ORDER BY name;";
            int rc = raw.sqlite3_prepare_v2(db, sql, out var stmt);
            if (rc != raw.SQLITE_OK)
            {
                stmt.Dispose();
                return list;
            }

            try
            {
                while (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
                {
                    string name = raw.sqlite3_column_text(stmt, 0).utf8_to_string();
                    string createSql = raw.sqlite3_column_type(stmt, 1) == raw.SQLITE_NULL
                        ? string.Empty
                        : raw.sqlite3_column_text(stmt, 1).utf8_to_string();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        list.Add((name, createSql));
                    }
                }
            }
            finally
            {
                stmt.Dispose();
            }

            return list;
        }

        private static Dictionary<string, List<string>> ReadAllUserIndexes(sqlite3 db)
        {
            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            const string sql = "SELECT tbl_name, sql FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%' AND sql IS NOT NULL;";
            int rc = raw.sqlite3_prepare_v2(db, sql, out var stmt);
            if (rc != raw.SQLITE_OK)
            {
                stmt.Dispose();
                return dict;
            }

            try
            {
                while (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
                {
                    string tblName = raw.sqlite3_column_text(stmt, 0).utf8_to_string();
                    string idxSql = raw.sqlite3_column_text(stmt, 1).utf8_to_string();
                    if (!dict.TryGetValue(tblName, out var list))
                    {
                        list = new List<string>();
                        dict[tblName] = list;
                    }
                    list.Add(idxSql);
                }
            }
            finally
            {
                stmt.Dispose();
            }

            return dict;
        }

        private static List<BackupTableColumnSchema> ReadTableColumns(sqlite3 db, string tableName)
        {
            var cols = new List<BackupTableColumnSchema>();
            string sql = $"PRAGMA table_info(\"{EscapeIdentifier(tableName)}\");";
            int rc = raw.sqlite3_prepare_v2(db, sql, out var stmt);
            if (rc != raw.SQLITE_OK)
            {
                stmt.Dispose();
                return cols;
            }

            try
            {
                while (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
                {
                    string colName = raw.sqlite3_column_text(stmt, 1).utf8_to_string();
                    string colType = raw.sqlite3_column_type(stmt, 2) == raw.SQLITE_NULL
                        ? "TEXT"
                        : raw.sqlite3_column_text(stmt, 2).utf8_to_string();
                    bool notNull = raw.sqlite3_column_int(stmt, 3) != 0;
                    string? dfltValue = raw.sqlite3_column_type(stmt, 4) == raw.SQLITE_NULL
                        ? null
                        : raw.sqlite3_column_text(stmt, 4).utf8_to_string();
                    bool isPk = raw.sqlite3_column_int(stmt, 5) != 0;

                    cols.Add(new BackupTableColumnSchema
                    {
                        Name = colName,
                        Type = colType,
                        NotNull = notNull,
                        DefaultValue = dfltValue,
                        IsPrimaryKey = isPk
                    });
                }
            }
            finally
            {
                stmt.Dispose();
            }

            return cols;
        }

        private static List<Dictionary<string, object?>> ReadTableRowsNormalized(sqlite3 db, string tableName, string appDataDir)
        {
            var rows = new List<Dictionary<string, object?>>();
            string sql = $"SELECT * FROM \"{EscapeIdentifier(tableName)}\";";
            int rc = raw.sqlite3_prepare_v2(db, sql, out var stmt);
            if (rc != raw.SQLITE_OK)
            {
                stmt.Dispose();
                return rows;
            }

            try
            {
                int colCount = raw.sqlite3_column_count(stmt);
                var colNames = new string[colCount];
                for (int i = 0; i < colCount; i++)
                {
                    colNames[i] = raw.sqlite3_column_name(stmt, i).utf8_to_string();
                }

                while (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < colCount; i++)
                    {
                        int colType = raw.sqlite3_column_type(stmt, i);
                        object? val = colType switch
                        {
                            raw.SQLITE_INTEGER => raw.sqlite3_column_int64(stmt, i),
                            raw.SQLITE_FLOAT => raw.sqlite3_column_double(stmt, i),
                            raw.SQLITE_TEXT => NormalizePathForExport(raw.sqlite3_column_text(stmt, i).utf8_to_string(), appDataDir),
                            raw.SQLITE_BLOB => raw.sqlite3_column_blob(stmt, i).ToArray(),
                            _ => null
                        };
                        row[colNames[i]] = val;
                    }
                    rows.Add(row);
                }
            }
            finally
            {
                stmt.Dispose();
            }

            return rows;
        }

        private static void NormalizeDatabasePathsInFile(string dbFilePath, string appDataDir, bool toRelative)
        {
            int rc = raw.sqlite3_open_v2(dbFilePath, out var db, raw.SQLITE_OPEN_READWRITE, null);
            if (rc != raw.SQLITE_OK)
            {
                db.Dispose();
                return;
            }

            try
            {
                var tables = ReadAllUserTables(db);
                foreach (var (tableName, _) in tables)
                {
                    var rows = ReadTableRowsNormalized(db, tableName, appDataDir);
                    if (rows.Count == 0)
                        continue;

                    if (toRelative)
                    {
                        UpsertRowsWithExactStrings(db, tableName, rows);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackupService] NormalizeDatabasePathsInFile warning: {ex.Message}");
            }
            finally
            {
                db.Dispose();
            }
        }

        private static void UpsertRowsWithExactStrings(sqlite3 db, string tableName, List<Dictionary<string, object?>> rows)
        {
            var tableColumns = ReadTableColumns(db, tableName)
                .ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var matchingKeys = row.Keys.Where(k => tableColumns.ContainsKey(k)).ToList();
                if (matchingKeys.Count == 0)
                    continue;

                var colNamesSql = string.Join(", ", matchingKeys.Select(k => $"\"{EscapeIdentifier(tableColumns[k].Name)}\""));
                var paramPlaceholders = string.Join(", ", matchingKeys.Select((_, i) => $"?{i + 1}"));
                string sql = $"INSERT OR REPLACE INTO \"{EscapeIdentifier(tableName)}\" ({colNamesSql}) VALUES ({paramPlaceholders});";

                if (raw.sqlite3_prepare_v2(db, sql, out var stmt) != raw.SQLITE_OK)
                {
                    stmt.Dispose();
                    continue;
                }

                try
                {
                    for (int i = 0; i < matchingKeys.Count; i++)
                    {
                        object? val = row[matchingKeys[i]];
                        if (val == null)
                            raw.sqlite3_bind_null(stmt, i + 1);
                        else if (val is long l)
                            raw.sqlite3_bind_int64(stmt, i + 1, l);
                        else if (val is double d)
                            raw.sqlite3_bind_double(stmt, i + 1, d);
                        else if (val is byte[] b)
                            raw.sqlite3_bind_blob(stmt, i + 1, b);
                        else
                            raw.sqlite3_bind_text(stmt, i + 1, val.ToString() ?? string.Empty);
                    }
                    raw.sqlite3_step(stmt);
                }
                finally
                {
                    stmt.Dispose();
                }
            }
        }

        private static void ExecuteNonQuery(sqlite3 db, string sql)
        {
            int rc = raw.sqlite3_prepare_v2(db, sql, out var stmt);
            if (rc != raw.SQLITE_OK)
            {
                string err = raw.sqlite3_errmsg(db).utf8_to_string();
                stmt.Dispose();
                throw new InvalidOperationException($"SQL Hatası: {err} ({sql})");
            }

            try
            {
                raw.sqlite3_step(stmt);
            }
            finally
            {
                stmt.Dispose();
            }
        }

        private static string InferSqlTypeFromJsonRows(List<Dictionary<string, object?>> rows, string key)
        {
            foreach (var row in rows)
            {
                if (!row.TryGetValue(key, out var val) || val == null)
                    continue;

                if (val is JsonElement je)
                {
                    if (je.ValueKind is JsonValueKind.Number)
                        return je.TryGetInt64(out _) ? "INTEGER" : "REAL";
                    if (je.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        return "INTEGER";
                    if (je.ValueKind is JsonValueKind.String)
                        return "TEXT";
                }
                else if (val is long or int or bool)
                {
                    return "INTEGER";
                }
                else if (val is double or float)
                {
                    return "REAL";
                }
            }
            return "TEXT";
        }

        private static string NormalizePathForExport(string value, string appDataDir)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            string normalizedValue = value.Replace('\\', '/');
            string normalizedAppDir = appDataDir.Replace('\\', '/').TrimEnd('/');

            if (normalizedValue.StartsWith(normalizedAppDir + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedValue.Substring(normalizedAppDir.Length + 1);
            }

            int mediaIdx = normalizedValue.LastIndexOf("/files/Media/", StringComparison.OrdinalIgnoreCase);
            if (mediaIdx >= 0)
            {
                return normalizedValue.Substring(mediaIdx + "/files/".Length);
            }

            int profileIdx = normalizedValue.LastIndexOf("/files/Profile/", StringComparison.OrdinalIgnoreCase);
            if (profileIdx >= 0)
            {
                return normalizedValue.Substring(profileIdx + "/files/".Length);
            }

            return value;
        }

        private static string ResolvePathForCurrentDevice(string value, string appDataDir)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            string normalized = value.Replace('\\', '/');

            if (normalized.StartsWith("Media/", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(normalized);
                return Path.Combine(appDataDir, "Media", fileName);
            }

            if (normalized.StartsWith("Profile/", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(normalized);
                return Path.Combine(appDataDir, "Profile", fileName);
            }

            int mediaIdx = normalized.LastIndexOf("/Media/", StringComparison.OrdinalIgnoreCase);
            if (mediaIdx >= 0 && normalized.Contains("/files/Media/", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(normalized);
                return Path.Combine(appDataDir, "Media", fileName);
            }

            int profileIdx = normalized.LastIndexOf("/Profile/", StringComparison.OrdinalIgnoreCase);
            if (profileIdx >= 0 && normalized.Contains("/files/Profile/", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(normalized);
                return Path.Combine(appDataDir, "Profile", fileName);
            }

            return value;
        }

        private static string MakeCreateTableIfNotExists(string createSql)
        {
            int idx = createSql.IndexOf("CREATE TABLE", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && !createSql.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                return createSql.Insert(idx + "CREATE TABLE".Length, " IF NOT EXISTS");
            }
            return createSql;
        }

        private static string MakeCreateIndexIfNotExists(string createIndexSql)
        {
            if (createIndexSql.Contains("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
                return createIndexSql;

            int uniqueIdx = createIndexSql.IndexOf("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase);
            if (uniqueIdx >= 0)
            {
                return createIndexSql.Insert(uniqueIdx + "CREATE UNIQUE INDEX".Length, " IF NOT EXISTS");
            }

            int idx = createIndexSql.IndexOf("CREATE INDEX", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return createIndexSql.Insert(idx + "CREATE INDEX".Length, " IF NOT EXISTS");
            }

            return createIndexSql;
        }

        private static string EscapeIdentifier(string identifier)
        {
            return identifier.Replace("\"", "\"\"");
        }

        #endregion

        #region Preferences Export / Import

        private static Dictionary<string, string?> ExportPreferences()
        {
            return new Dictionary<string, string?>
            {
                ["ServerIp"] = Preferences.Default.Get("ServerIp", RelayService.GetDefaultServerIp()),
                ["RecentEmojis"] = Preferences.Default.Get("RecentEmojis", "💗,🙃,😋"),
                [CameraView.CaptureFromPreviewPreferenceKey] = Preferences.Default.Get(CameraView.CaptureFromPreviewPreferenceKey, CameraView.CaptureFromPreviewDefault).ToString(),
                [CameraView.ZoomSliderPreferenceKey] = Preferences.Default.Get(CameraView.ZoomSliderPreferenceKey, CameraView.ZoomSliderDefault).ToString(),
                [CameraView.ExposureSliderPreferenceKey] = Preferences.Default.Get(CameraView.ExposureSliderPreferenceKey, CameraView.ExposureSliderDefault).ToString(),
                [CameraView.KeepZoomPreferenceKey] = Preferences.Default.Get(CameraView.KeepZoomPreferenceKey, CameraView.KeepZoomDefault).ToString(),
                [CameraView.KeepExposurePreferenceKey] = Preferences.Default.Get(CameraView.KeepExposurePreferenceKey, CameraView.KeepExposureDefault).ToString(),
                [Pocket.Client.PageModels.AppShellModel.AccentColorPreferenceKey] = Pocket.Client.PageModels.AppShellModel.GetSavedAccentColorHex()
            };
        }

        private static void ImportPreferences(Dictionary<string, string?> prefs)
        {
            if (prefs.TryGetValue("ServerIp", out var serverIp) && !string.IsNullOrWhiteSpace(serverIp))
            {
                Preferences.Default.Set("ServerIp", serverIp);
            }

            if (prefs.TryGetValue("RecentEmojis", out var recentEmojis) && !string.IsNullOrWhiteSpace(recentEmojis))
            {
                Preferences.Default.Set("RecentEmojis", recentEmojis);
            }

            if (prefs.TryGetValue(Pocket.Client.PageModels.AppShellModel.AccentColorPreferenceKey, out var accentHex) &&
                !string.IsNullOrWhiteSpace(accentHex))
            {
                var normalized = Pocket.Client.PageModels.AppShellModel.NormalizeAccentColorHex(accentHex);
                Preferences.Default.Set(Pocket.Client.PageModels.AppShellModel.AccentColorPreferenceKey, normalized);
                Pocket.Client.PageModels.AppShellModel.ApplyAccentColorResources(normalized);
            }

            SetBoolPrefIfPresent(prefs, CameraView.CaptureFromPreviewPreferenceKey);
            SetBoolPrefIfPresent(prefs, CameraView.ZoomSliderPreferenceKey);
            SetBoolPrefIfPresent(prefs, CameraView.ExposureSliderPreferenceKey);
            SetBoolPrefIfPresent(prefs, CameraView.KeepZoomPreferenceKey);
            SetBoolPrefIfPresent(prefs, CameraView.KeepExposurePreferenceKey);
        }

        private static void SetBoolPrefIfPresent(Dictionary<string, string?> prefs, string key)
        {
            if (prefs.TryGetValue(key, out var rawVal) && bool.TryParse(rawVal, out bool parsed))
            {
                Preferences.Default.Set(key, parsed);
            }
        }

        #endregion

        #region File System Utilities

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            return sb.ToString();
        }

        private static void ClearDirectoryFiles(string dirPath)
        {
            if (!Directory.Exists(dirPath))
                return;

            foreach (var file in Directory.GetFiles(dirPath))
            {
                TryDeleteFile(file);
            }
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }

        #endregion
    }
}
