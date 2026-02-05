using Microsoft.Extensions.Logging;
using UmaDecryptor.Commands;
using UmaDecryptor.Database;
using Microsoft.Data.Sqlite;

namespace UmaDecryptor.Services;

/// <summary>
/// Single database file decryption service
/// </summary>
public class DecryptDbService
{
    private readonly ILogger<DecryptDbService> _logger;
    private readonly UmaDatabaseKeyManager _keyManager;
    private readonly DatabaseFileProcessor _fileProcessor;

    public DecryptDbService(ILogger<DecryptDbService> logger)
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
    /// Decrypt single database file
    /// </summary>
    public async Task ProcessAsync(DecryptDbOptions options)
    {
        _logger.LogInformation("Starting database decryption...");
        _logger.LogInformation("Input: {InputPath}", options.InputPath);
        _logger.LogInformation("Output: {OutputPath}", options.OutputPath);
        _logger.LogInformation("Region: {Region}", options.Region);

        try
        {
            // Validate input file
            if (!File.Exists(options.InputPath))
            {
                _logger.LogError("Input file does not exist: {InputPath}", options.InputPath);
                return;
            }

            var fileInfo = new FileInfo(options.InputPath);
            _logger.LogInformation("Input file size: {FileSize:N0} bytes ({SizeMB:F2} MB)", 
                fileInfo.Length, fileInfo.Length / (1024.0 * 1024.0));

            // Get decryption key
            byte[] decryptionKey;
            if (!string.IsNullOrEmpty(options.CustomKey))
            {
                _logger.LogInformation("Using custom decryption key");
                decryptionKey = ParseHexKey(options.CustomKey);
            }
            else
            {
                _logger.LogInformation("Using default decryption key for region: {Region}", options.Region);
                decryptionKey = _keyManager.GetDatabaseDecryptionKey(options.Region);
            }

            // Create output directory
            var outputDir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Execute decryption
            _logger.LogInformation("Starting decryption process...");
            await DecryptSingleDatabaseAsync(options.InputPath, options.OutputPath, decryptionKey, options.CipherIndex);

            // Verify results
            if (File.Exists(options.OutputPath))
            {
                var outputFileInfo = new FileInfo(options.OutputPath);
                _logger.LogInformation("Decryption completed successfully!");
                _logger.LogInformation("Output file size: {FileSize:N0} bytes ({SizeMB:F2} MB)", 
                    outputFileInfo.Length, outputFileInfo.Length / (1024.0 * 1024.0));

                // Verify decryption results
                var isValid = await _fileProcessor.ValidateDecryptedFileAsync(options.OutputPath);
                if (isValid)
                {
                    _logger.LogInformation("✅ Decrypted database validation passed");
                }
                else
                {
                    _logger.LogWarning("⚠️ Decrypted database validation failed - file may be corrupted");
                }
            }
            else
            {
                _logger.LogError("❌ Decryption failed - output file was not created");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database decryption failed");
            throw;
        }
    }

    /// <summary>
    /// Core logic for decrypting single database file
    /// </summary>
    private async Task DecryptSingleDatabaseAsync(string inputPath, string outputPath, byte[] key, int cipherIndex)
    {
        await Task.Run(() =>
        {
            IntPtr db = IntPtr.Zero;

            try
            {
                // Open encrypted database
                db = Sqlite3MC.Open(inputPath);

                // Set cipher index
                int cfgRc = Sqlite3MC.MC_Config(db, "cipher", cipherIndex);

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
                    throw new InvalidOperationException($"Database validation failed after key setup: {validateErr}");
                }

                _logger.LogInformation("✅ Successfully opened and validated encrypted database");

                // Read database content (all tables)
                var allTablesData = ReadAllTablesFromDatabase(db);
                _logger.LogInformation("📊 Read data from {TableCount} tables", allTablesData.Count);

                // Create decrypted database (use output path directly, no modification)
                CreateDecryptedDatabase(outputPath, allTablesData);

                _logger.LogInformation("✅ Successfully created decrypted database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database decryption process");
                throw;
            }
            finally
            {
                if (db != IntPtr.Zero)
                {
                    try
                    {
                        Sqlite3MC.Close(db);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error closing database connection");
                    }
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

        // Set working directory to executable file location
        var originalWorkingDir = Environment.CurrentDirectory;
        try
        {
            var exeDir = System.AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(exeDir))
            {
                Environment.CurrentDirectory = exeDir;
            }

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
        finally
        {
            Environment.CurrentDirectory = originalWorkingDir;
        }
    }

    /// <summary>
    /// Parse hexadecimal key string
    /// </summary>
    private byte[] ParseHexKey(string hexKey)
    {
        try
        {
            // Remove possible prefix and spaces
            hexKey = hexKey.Replace("0x", "").Replace(" ", "").Replace("-", "");

            if (hexKey.Length % 2 != 0)
            {
                throw new ArgumentException("Hex key length must be even");
            }

            byte[] keyBytes = new byte[hexKey.Length / 2];
            for (int i = 0; i < keyBytes.Length; i++)
            {
                keyBytes[i] = Convert.ToByte(hexKey.Substring(i * 2, 2), 16);
            }

            return keyBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse hex key: {HexKey}", hexKey);
            throw new ArgumentException($"Invalid hex key format: {hexKey}", ex);
        }
    }
}