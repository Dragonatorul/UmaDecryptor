using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;

namespace UmaDecryptor.Database;

/// <summary>
/// Database file processor - responsible for decrypting UMA database using sqlite3mc
/// </summary>
public class DatabaseFileProcessor
{
    private readonly ILogger<DatabaseFileProcessor> _logger;

    public DatabaseFileProcessor(ILogger<DatabaseFileProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Decrypt database file - use sqlite3mc to read encrypted database and generate decrypted version
    /// </summary>
    public async Task DecryptDatabaseFileAsync(string inputFilePath, string outputFilePath, byte[] key)
    {
        _logger.LogInformation("Decrypting database: {InputFile} -> {OutputFile}", inputFilePath, outputFilePath);

        // Check if it's a meta file
        var isMetaFile = Path.GetFileName(inputFilePath).Equals("meta", StringComparison.OrdinalIgnoreCase);
        
        if (isMetaFile)
        {
            await DecryptMetaFileAsync(inputFilePath, outputFilePath, key);
        }
        else
        {
            // For other types of database files, processing logic can be extended
            await DecryptGenericDatabaseAsync(inputFilePath, outputFilePath, key);
        }
    }

    /// <summary>
    /// Decrypt Meta file - use sqlite3mc to read encrypted database
    /// </summary>
    private async Task DecryptMetaFileAsync(string inputFilePath, string outputFilePath, byte[] key)
    {
        _logger.LogInformation("Processing meta database file with sqlite3mc...");
        
        await Task.Run(() =>
        {
            IntPtr db = IntPtr.Zero;
            
            try
            {
                // Open encrypted database
                _logger.LogDebug("Opening encrypted database: {InputFile}", inputFilePath);
                db = Sqlite3MC.Open(inputFilePath);

                // Set cipher index (according to your code, use cipher index 3)
                int cfgRc = Sqlite3MC.MC_Config(db, "cipher", 3);
                _logger.LogDebug("sqlite3mc_config(cipher, 3) returned: {ReturnCode}", cfgRc);

                // Set decryption key
                int rcKey = Sqlite3MC.Key_SetBytes(db, key);
                if (rcKey != Sqlite3MC.SQLITE_OK)
                {
                    string em = Sqlite3MC.GetErrMsg(db);
                    throw new InvalidOperationException($"sqlite3_key returned rc={rcKey}, errmsg={em}");
                }

                // Verify if database is readable
                if (!Sqlite3MC.ValidateReadable(db, out string? validateErr))
                {
                    throw new InvalidOperationException($"Database validation failed after key setup: {validateErr}");
                }

                _logger.LogInformation("Successfully opened and validated encrypted database");

                // Read data and create decrypted database
                var entries = ReadMetaEntriesFromDatabase(db);
                _logger.LogInformation("Read {EntryCount} entries from encrypted database", entries.Count);

                // Create decrypted database
                CreateDecryptedDatabase(outputFilePath, entries);
                
                _logger.LogInformation("Successfully created decrypted database: {OutputFile}", outputFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt meta database");
                throw;
            }
            finally
            {
                if (db != IntPtr.Zero)
                {
                    try 
                    { 
                        Sqlite3MC.Close(db);
                        _logger.LogDebug("Closed encrypted database connection");
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
    /// Read Meta entries from encrypted database
    /// </summary>
    private Dictionary<string, UmaDatabaseEntry> ReadMetaEntriesFromDatabase(IntPtr db)
    {
        var entries = new Dictionary<string, UmaDatabaseEntry>(StringComparer.Ordinal);
        
        // Query all columns, more concise and flexible
        string sql = "SELECT * FROM a";
        
        _logger.LogDebug("Executing query: {Sql}", sql);
        
        try
        {
            Sqlite3MC.ForEachRow(sql, db, (stmt) =>
            {
                try
                {
                    // Read column data (in standard order: m,n,h,c,d,e)
                    string? m = Sqlite3MC.ColumnText(stmt, 0); // type
                    string? n = Sqlite3MC.ColumnText(stmt, 1); // name
                    string? h = Sqlite3MC.ColumnText(stmt, 2); // url
                    string? c = Sqlite3MC.ColumnText(stmt, 3); // checksum
                    string? d = Sqlite3MC.ColumnText(stmt, 4); // dependencies
                    string? e = Sqlite3MC.ColumnText(stmt, 5); // key

                    // Validate required fields
                    if (string.IsNullOrEmpty(m))
                    {
                        _logger.LogWarning("Skipping row: empty type string (m)");
                        return;
                    }

                    if (string.IsNullOrEmpty(n))
                    {
                        _logger.LogWarning("Skipping row: empty name string (n)");
                        return;
                    }

                    // Create entry (including all columns)
                    var entry = new UmaDatabaseEntry
                    {
                        Type = m,
                        Name = n,
                        Url = h ?? string.Empty,
                        Checksum = c,
                        Dependencies = d ?? string.Empty,
                        Key = e
                    };

                    // Add to dictionary (deduplication)
                    if (!entries.ContainsKey(entry.Name))
                    {
                        entries.Add(entry.Name, entry);
                    }
                }
                catch (Exception exRow)
                {
                    _logger.LogError(exRow, "Error reading row from database");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing database query");
            throw;
        }

        return entries;
    }

    /// <summary>
    /// Create decrypted SQLite database
    /// </summary>
    private void CreateDecryptedDatabase(string outputPath, Dictionary<string, UmaDatabaseEntry> entries)
    {
        _logger.LogDebug("Creating decrypted database: {OutputPath}", outputPath);
        
        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Delete existing file
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        // Create SQLite connection string
        var connectionString = $"Data Source={outputPath}";
        
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        
        try
        {
            // Create table structure (complete 6 columns)
            var createTableSql = @"
                CREATE TABLE a (
                    m TEXT,  -- type
                    n TEXT,  -- name  
                    h TEXT,  -- url
                    c TEXT,  -- checksum
                    d TEXT,  -- dependencies
                    e TEXT   -- key
                );
                
                CREATE INDEX IF NOT EXISTS idx_name ON a(n);
                CREATE INDEX IF NOT EXISTS idx_type ON a(m);
                CREATE INDEX IF NOT EXISTS idx_checksum ON a(c);
            ";
            
            using var createCommand = new SqliteCommand(createTableSql, connection);
            createCommand.ExecuteNonQuery();
            
            _logger.LogDebug("Created table structure with all 6 columns in decrypted database");

            // Insert data
            using var transaction = connection.BeginTransaction();
            
            var insertSql = "INSERT INTO a (m, n, h, c, d, e) VALUES (@m, @n, @h, @c, @d, @e)";
            using var insertCommand = new SqliteCommand(insertSql, connection);
            
            insertCommand.Parameters.Add("@m", SqliteType.Text);
            insertCommand.Parameters.Add("@n", SqliteType.Text);
            insertCommand.Parameters.Add("@h", SqliteType.Text);
            insertCommand.Parameters.Add("@c", SqliteType.Text);
            insertCommand.Parameters.Add("@d", SqliteType.Text);
            insertCommand.Parameters.Add("@e", SqliteType.Text);

            int insertedCount = 0;
            foreach (var entry in entries.Values)
            {
                insertCommand.Parameters["@m"].Value = entry.Type;
                insertCommand.Parameters["@n"].Value = entry.Name;
                insertCommand.Parameters["@h"].Value = entry.Url;
                insertCommand.Parameters["@c"].Value = entry.Checksum ?? string.Empty;
                insertCommand.Parameters["@d"].Value = entry.Dependencies;
                insertCommand.Parameters["@e"].Value = entry.Key ?? string.Empty;

                insertCommand.ExecuteNonQuery();
                insertedCount++;
            }

            transaction.Commit();
            _logger.LogInformation("Inserted {InsertedCount} entries into decrypted database", insertedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating decrypted database");
            throw;
        }
    }

    /// <summary>
    /// Decrypt generic database file (for extension)
    /// </summary>
    private async Task DecryptGenericDatabaseAsync(string inputFilePath, string outputFilePath, byte[] key)
    {
        _logger.LogInformation("Processing generic database file: {InputFile}", inputFilePath);
        
        // TODO: Implement decryption logic for other types of databases as needed
        await Task.Run(() =>
        {
            // Temporarily copy file directly
            File.Copy(inputFilePath, outputFilePath, overwrite: true);
            _logger.LogInformation("Copied database file: {OutputFile}", outputFilePath);
        });
    }

    /// <summary>
    /// Validate decryption results
    /// </summary>
    public async Task<bool> ValidateDecryptedFileAsync(string filePath)
    {
        try
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogError("Decrypted file does not exist: {FilePath}", filePath);
                    return false;
                }

                // Check file size
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    _logger.LogError("Decrypted file is empty: {FilePath}", filePath);
                    return false;
                }

                // Try to connect to SQLite database for validation
                return ValidateSQLiteDatabase(filePath);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating decrypted file: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Validate SQLite database connection
    /// </summary>
    private bool ValidateSQLiteDatabase(string dbPath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            
            // Try to get table count in database to validate database integrity
            using var command = new SqliteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';", connection);
            var tableCount = command.ExecuteScalar();
            
            connection.Close();
            
            _logger.LogInformation("SQLite database validation successful - found {TableCount} tables", tableCount);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQLite database validation failed");
            return false;
        }
    }
}