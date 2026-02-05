using Microsoft.Extensions.Logging;
using System.Data.SQLite;
using Microsoft.Data.Sqlite;
using UmaDecryptor.Core;

namespace UmaDecryptor.Database;

/// <summary>
/// Database decryptor - core database decryption functionality
/// </summary>
public class DatabaseDecryptor
{
    private readonly ILogger<DatabaseDecryptor> _logger;
    private readonly UmaDatabaseKeyManager _keyManager;
    private readonly DatabaseFileProcessor _fileProcessor;

    public DatabaseDecryptor(ILogger<DatabaseDecryptor> logger)
    {
        _logger = logger;
        
        // Create dedicated logger for child components
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Information);
        });
        
        _keyManager = new UmaDatabaseKeyManager(loggerFactory.CreateLogger<UmaDatabaseKeyManager>());
        _fileProcessor = new DatabaseFileProcessor(loggerFactory.CreateLogger<DatabaseFileProcessor>());
    }

    /// <summary>
    /// Decrypt all database files
    /// </summary>
    public async Task DecryptDatabasesAsync(string inputPath, string outputPath, Region region = Region.Japan)
    {
        _logger.LogInformation("Starting database decryption process...");
        _logger.LogInformation("Region: {Region}", region);

        // Get database decryption key
        var decryptionKey = _keyManager.GetDatabaseDecryptionKey(region);
        _logger.LogDebug("Database decryption key obtained");

        // Scan database files that need decryption
        var databaseFiles = await ScanDatabaseFilesAsync(inputPath);
        _logger.LogInformation("Found {Count} database files to decrypt", databaseFiles.Count);

        // Process meta file decryption (output directly to root directory)
        var metaFiles = databaseFiles.Where(f => f.DatabaseType == DatabaseType.Meta).ToList();
        if (metaFiles.Any())
        {
            _logger.LogInformation("Processing meta database files...");
            var metaDecryptionTasks = metaFiles.Select(async dbFile =>
            {
                await DecryptSingleDatabaseAsync(dbFile, outputPath, decryptionKey);
            });
            await Task.WhenAll(metaDecryptionTasks);
        }

        // Process master folder - copy intact
        await CopyMasterDirectoryAsync(inputPath, outputPath);

        // Process other database files (if any)
        var otherFiles = databaseFiles.Where(f => f.DatabaseType != DatabaseType.Meta).ToList();
        if (otherFiles.Any())
        {
            _logger.LogInformation("Processing other database files...");
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
            var decryptionTasks = otherFiles.Select(async dbFile =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await DecryptSingleDatabaseAsync(dbFile, outputPath, decryptionKey);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(decryptionTasks);
        }
        
        _logger.LogInformation("Database processing completed successfully!");
    }

    /// <summary>
    /// Scan database files in input directory
    /// </summary>
    private async Task<List<DatabaseFileInfo>> ScanDatabaseFilesAsync(string inputPath)
    {
        return await Task.Run(() =>
        {
            var databaseFiles = new List<DatabaseFileInfo>();
            
            // Check meta file (encrypted database file directly in root directory)
            var metaFile = Path.Combine(inputPath, "meta");
            if (File.Exists(metaFile))
            {
                _logger.LogInformation("Found meta database file: {FileSize:N0} bytes", new FileInfo(metaFile).Length);
                databaseFiles.Add(new DatabaseFileInfo
                {
                    FilePath = metaFile,
                    RelativePath = "meta",
                    FileSize = new FileInfo(metaFile).Length,
                    IsEncrypted = true, // Meta files are always encrypted
                    DatabaseType = DatabaseType.Meta
                });
            }

            // Scan other possible database directories
            var searchDirectories = new[]
            {
                Path.Combine(inputPath, "master"),
                Path.Combine(inputPath, "dat")
            };

            foreach (var dir in searchDirectories.Where(Directory.Exists))
            {
                var dirName = Path.GetFileName(dir);
                // Find database files
                var patterns = new[] { "*.db", "*.sqlite", "*.dat" };
                
                foreach (var pattern in patterns)
                {
                    var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (IsDatabaseFile(file))
                        {
                            var dbType = dirName.ToLower() switch
                            {
                                "master" => DatabaseType.Master,
                                "dat" => DatabaseType.Data,
                                _ => DatabaseType.Unknown
                            };

                            databaseFiles.Add(new DatabaseFileInfo
                            {
                                FilePath = file,
                                RelativePath = Path.GetRelativePath(inputPath, file),
                                FileSize = new FileInfo(file).Length,
                                IsEncrypted = CheckIfEncrypted(file),
                                DatabaseType = dbType
                            });
                        }
                    }
                }
            }

            return databaseFiles;
        });
    }

    /// <summary>
    /// Decrypt single database file
    /// </summary>
    private async Task DecryptSingleDatabaseAsync(DatabaseFileInfo dbInfo, string outputPath, byte[] key)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
        }

        _logger.LogInformation("Decrypting {DatabaseType} database: {RelativePath} ({FileSize:N0} bytes)", 
            dbInfo.DatabaseType, dbInfo.RelativePath, dbInfo.FileSize);

        try
        {
            if (!dbInfo.IsEncrypted)
            {
                await CopyUnencryptedDatabaseAsync(dbInfo, outputPath);
                return;
            }

            // Determine output filename and path based on database type
            string outputFilePath;
            if (dbInfo.DatabaseType == DatabaseType.Meta)
            {
                // Meta files output directly to root directory, no suffix added
                outputFilePath = Path.Combine(outputPath, "meta");
            }
            else
            {
                // Other files keep original logic, placed in databases folder
                var dbOutputPath = Path.Combine(outputPath, "databases");
                Directory.CreateDirectory(dbOutputPath);
                outputFilePath = Path.Combine(dbOutputPath, Path.GetFileName(dbInfo.FilePath));
            }
            
            // For meta files, use new logic to read all tables completely
            if (dbInfo.DatabaseType == DatabaseType.Meta)
            {
                await DecryptMetaWithAllTablesAsync(dbInfo.FilePath, outputFilePath, key);
            }
            else
            {
                // Execute decryption operation (other files use original logic)
                await _fileProcessor.DecryptDatabaseFileAsync(dbInfo.FilePath, outputFilePath, key);
            }
            
            // Verify decryption results
            if (await _fileProcessor.ValidateDecryptedFileAsync(outputFilePath))
            {
                _logger.LogInformation("Successfully decrypted {DatabaseType}: {RelativePath} -> {OutputFile}", 
                    dbInfo.DatabaseType, dbInfo.RelativePath, Path.GetFileName(outputFilePath));
            }
            else
            {
                _logger.LogWarning("Decryption completed but validation failed for: {RelativePath}", 
                    dbInfo.RelativePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt {DatabaseType} database: {RelativePath}", 
                dbInfo.DatabaseType, dbInfo.RelativePath);
            throw;
        }
    }

    /// <summary>
    /// Copy unencrypted database file
    /// </summary>
    private async Task CopyUnencryptedDatabaseAsync(DatabaseFileInfo dbInfo, string outputPath)
    {
        var outputFilePath = Path.Combine(outputPath, Path.GetFileName(dbInfo.FilePath));
        await Task.Run(() => File.Copy(dbInfo.FilePath, outputFilePath, overwrite: true));
    }

    /// <summary>
    /// Copy master folder to output directory
    /// </summary>
    private async Task CopyMasterDirectoryAsync(string inputPath, string outputPath)
    {
        var masterInputPath = Path.Combine(inputPath, "master");
        var masterOutputPath = Path.Combine(outputPath, "master");

        if (!Directory.Exists(masterInputPath))
        {
            _logger.LogWarning("Master directory not found: {MasterPath}", masterInputPath);
            return;
        }

        _logger.LogInformation("Copying master directory...");
        _logger.LogInformation("Source: {SourcePath}", masterInputPath);
        _logger.LogInformation("Target: {TargetPath}", masterOutputPath);

        await Task.Run(() =>
        {
            try
            {
// Create target directory
                Directory.CreateDirectory(masterOutputPath);

                // Get all files and subdirectories in source directory
                var sourceInfo = new DirectoryInfo(masterInputPath);
                CopyDirectoryRecursively(sourceInfo, masterOutputPath);

                // Count copy results
                var copiedFiles = Directory.GetFiles(masterOutputPath, "*", SearchOption.AllDirectories);
                var totalSize = copiedFiles.Sum(file => new FileInfo(file).Length);

                _logger.LogInformation("✅ Master directory copied successfully!");
                _logger.LogInformation("📊 Copied {FileCount} files, total size: {TotalSize:N0} bytes ({SizeMB:F2} MB)", 
                    copiedFiles.Length, totalSize, totalSize / (1024.0 * 1024.0));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy master directory");
                throw;
            }
        });
    }

    /// <summary>
    /// Recursively copy directory and its contents
    /// </summary>
    private void CopyDirectoryRecursively(DirectoryInfo sourceDir, string targetDirPath)
    {
        // Create target directory
        Directory.CreateDirectory(targetDirPath);

        // Copy all files
        foreach (var file in sourceDir.GetFiles())
        {
            var targetFilePath = Path.Combine(targetDirPath, file.Name);
            
            try
            {
                file.CopyTo(targetFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy file: {FileName}", file.Name);
                // Continue processing other files, don't interrupt entire process
            }
        }

        // Recursively copy all subdirectories
        foreach (var subDir in sourceDir.GetDirectories())
        {
            var targetSubDirPath = Path.Combine(targetDirPath, subDir.Name);
            
            try
            {
                CopyDirectoryRecursively(subDir, targetSubDirPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy subdirectory: {SubDirName}", subDir.Name);
                // Continue processing other directories, don't interrupt entire process
            }
        }
    }

    /// <summary>
    /// Check if file is a database file
    /// </summary>
    private bool IsDatabaseFile(string filePath)
    {
        // Simple database file identification logic
        // Can be identified by file extension or file header
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        
        // Common database file extensions or special filenames
        return extension == ".db" || extension == ".sqlite" || extension == ".dat" ||
               fileName.Contains("master") || fileName.Contains("meta");
    }

    /// <summary>
    /// Check if database file is encrypted
    /// </summary>
    private bool CheckIfEncrypted(string filePath)
    {
        // For UMA games, we assume most files need special processing
        // Meta files are definitely encrypted, other files may need detection
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        
        if (fileName == "meta")
        {
            return true; // Meta files are always encrypted
        }
        
        // Encryption detection logic for other files can be improved later
        // Currently return false, indicating direct copy
        return false;
    }

    /// <summary>
    /// Decrypt meta database and read all tables (new logic)
    /// </summary>
    private async Task DecryptMetaWithAllTablesAsync(string inputFilePath, string outputFilePath, byte[] key)
    {
        _logger.LogInformation("Processing meta database with complete table reading...");

        await Task.Run(() =>
        {
            IntPtr db = IntPtr.Zero;

            try
            {
                // Open encrypted database
                db = Sqlite3MC.Open(inputFilePath);

                // Set cipher configuration
                int cfgRc = Sqlite3MC.MC_Config(db, "cipher", 3);

                // Set decryption key
                int rcKey = Sqlite3MC.Key_SetBytes(db, key);
                if (rcKey != Sqlite3MC.SQLITE_OK)
                {
                    string em = Sqlite3MC.GetErrMsg(db);
                    throw new InvalidOperationException($"sqlite3_key returned rc={rcKey}, errmsg={em}");
                }

                // Verify database readability
                if (!Sqlite3MC.ValidateReadable(db, out string? validateErr))
                {
                    throw new InvalidOperationException($"Database validation failed: {validateErr}");
                }

                _logger.LogInformation("✅ Successfully opened encrypted meta database");

                // Read data from all tables
                var allTablesData = ReadAllTablesFromDatabase(db);
                _logger.LogInformation("📊 Read data from {TableCount} tables", allTablesData.Count);

                // Create decrypted database
                CreateDecryptedDatabase(outputFilePath, allTablesData);

                _logger.LogInformation("🎉 Successfully created decrypted meta database with all tables");
            }
            finally
            {
                if (db != IntPtr.Zero)
                {
                    Sqlite3MC.Close(db);
                }
            }
        });
    }

    /// <summary>
    /// Read data from all tables in database
    /// </summary>
    private Dictionary<string, List<Dictionary<string, object>>> ReadAllTablesFromDatabase(IntPtr db)
    {
        var allTablesData = new Dictionary<string, List<Dictionary<string, object>>>();

        try
        {
            // First get all table names
            var tableNames = new List<string>();
            const string getTablesQuery = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            
            Sqlite3MC.ForEachRow(getTablesQuery, db, (stmt) =>
            {
                try
                {
                    string? tableName = Sqlite3MC.ColumnText(stmt, 0);
                    if (!string.IsNullOrEmpty(tableName))
                    {
                        tableNames.Add(tableName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading table name");
                }
            });

            _logger.LogInformation("🔍 Found {TableCount} tables: {Tables}", 
                tableNames.Count, string.Join(", ", tableNames));

            // Read data from each table
            foreach (var tableName in tableNames)
            {
                var tableData = new List<Dictionary<string, object>>();
                
                try
                {
                    var query = $"SELECT * FROM [{tableName}]";

                    Sqlite3MC.ForEachRow(query, db, (stmt) =>
                    {
                        try
                        {
                            var entry = new Dictionary<string, object>();
                            
                            // Get column count
                            int columnCount = Sqlite3MC.ColumnCount(stmt);
                            
                            for (int i = 0; i < columnCount; i++)
                            {
                                string columnName = Sqlite3MC.ColumnName(stmt, i) ?? $"column_{i}";
                                string? value = Sqlite3MC.ColumnText(stmt, i);
                                entry[columnName] = value ?? string.Empty;
                            }
                            
                            tableData.Add(entry);
                        }
                        catch (Exception exRow)
                        {
                            _logger.LogError(exRow, "Error reading row from table {TableName}", tableName);
                        }
                    });

                    allTablesData[tableName] = tableData;
                    _logger.LogInformation("📋 Table '{TableName}': {RowCount} rows", tableName, tableData.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to read table {TableName}", tableName);
                    // Continue processing other tables, don't let one table's error affect the whole
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to read table information from database");
            throw;
        }

        return allTablesData;
    }

    /// <summary>
    /// Create decrypted SQLite database (containing all tables)
    /// </summary>
    private void CreateDecryptedDatabase(string outputPath, Dictionary<string, List<Dictionary<string, object>>> allTablesData)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
        }

        _logger.LogInformation("Creating decrypted database with {TableCount} tables: {OutputPath}", allTablesData.Count, outputPath);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Delete existing file
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        // Create SQLite connection string
        var absolutePath = Path.GetFullPath(outputPath);
        
        // Use Microsoft.Data.Sqlite to create output database
        var connectionString = $"Data Source={absolutePath}";

        try 
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            try
            {
                using var transaction = connection.BeginTransaction();

            // Create table structure and insert data for each table
            foreach (var tableData in allTablesData)
            {
                string tableName = tableData.Key;
                var rows = tableData.Value;
                
                if (rows.Count == 0)
                {
                    _logger.LogWarning("⚠️ Table '{TableName}' is empty, skipping", tableName);
                    continue;
                }

                try
                {
                    // Infer column structure from first row of data
                    var firstRow = rows[0];
                    var columns = firstRow.Keys.ToList();

                    // Create table structure
                    var columnDefs = columns.Select(col => $"[{col}] TEXT").ToList();
                    var createTableSql = $"CREATE TABLE [{tableName}] ({string.Join(", ", columnDefs)});";

                    using var createCommand = new SqliteCommand(createTableSql, connection, transaction);
                    createCommand.ExecuteNonQuery();

                    _logger.LogDebug("Created table '{TableName}' with {ColumnCount} columns: {Columns}", 
                        tableName, columns.Count, string.Join(", ", columns));

                    // Bulk insert data
                    var paramNames = columns.Select(col => $"@{col}").ToList();
                    var insertSql = $"INSERT INTO [{tableName}] ([{string.Join("], [", columns)}]) VALUES ({string.Join(", ", paramNames)});";
                    
                    using var insertCommand = new SqliteCommand(insertSql, connection, transaction);

                    // Add parameters
                    foreach (var col in columns)
                    {
                        insertCommand.Parameters.Add($"@{col}", SqliteType.Text);
                    }

                    int insertedCount = 0;
                    foreach (var row in rows)
                    {
                        // Set parameter values
                        foreach (var col in columns)
                        {
                            var value = row.ContainsKey(col) ? row[col] : DBNull.Value;
                            if (value == DBNull.Value)
                            {
                                insertCommand.Parameters[$"@{col}"].Value = DBNull.Value;
                            }
                            else
                            {
                                insertCommand.Parameters[$"@{col}"].Value = value?.ToString() ?? string.Empty;
                            }
                        }

                        insertCommand.ExecuteNonQuery();
                        insertedCount++;
                    }

                    _logger.LogInformation("✅ Table '{TableName}': inserted {InsertedCount} rows", tableName, insertedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to create table '{TableName}'", tableName);
                    // Continue processing other tables
                }
            }

            transaction.Commit();
            _logger.LogInformation("🎉 Successfully created decrypted database with {TableCount} tables", allTablesData.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating decrypted database");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating SQLite connection");
            throw;
        }
    }
}

/// <summary>
/// Database file information
/// </summary>
public class DatabaseFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool IsEncrypted { get; set; }
    public DatabaseType DatabaseType { get; set; } = DatabaseType.Unknown;
}

/// <summary>
/// Database type enumeration
/// </summary>
public enum DatabaseType
{
    Unknown,
    Meta,      // Main meta database file
    Master,    // Database files in master directory
    Data       // Data files in dat directory
}