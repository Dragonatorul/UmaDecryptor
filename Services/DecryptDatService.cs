using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using UmaDecryptor.Commands;
using UmaDecryptor.Crypto;
using UmaDecryptor.Database;

namespace UmaDecryptor.Services;

/// <summary>
/// Dat file decryption service
/// </summary>
public class DecryptDatService
{
    private readonly ILogger<DecryptDatService> _logger;
    private readonly UmaDatabaseKeyManager _keyManager;

    public DecryptDatService(ILogger<DecryptDatService> logger, UmaDatabaseKeyManager keyManager)
    {
        _logger = logger;
        _keyManager = keyManager;
    }

    /// <summary>
    /// Execute dat file decryption
    /// </summary>
    public async Task<int> ExecuteAsync(DecryptDatOptions options)
    {
        try
        {
            // Calculate whether to skip existing files: if overwrite is set, do not skip
            bool skipExisting = !options.Overwrite;

            _logger.LogInformation("🚀 Starting dat files decryption");
            _logger.LogInformation("📂 Input path: {InputPath}", options.InputPath);
            _logger.LogInformation("📁 Output path: {OutputPath}", options.OutputPath);
            _logger.LogInformation("🗃️ Meta database: {MetaPath}", options.MetaPath);
            _logger.LogInformation("🌍 Region: {Region}", options.Region);
            if (skipExisting)
            {
                _logger.LogInformation("⏭️ Skip existing files: enabled (incremental mode)");
            }
            else
            {
                _logger.LogInformation("🔄 Overwrite mode: processing all files");
            }

            // 验证输入路径
            if (!Directory.Exists(options.InputPath))
            {
                _logger.LogError("❌ Input directory does not exist: {InputPath}", options.InputPath);
                return -1;
            }

            if (!File.Exists(options.MetaPath))
            {
                _logger.LogError("❌ Meta database file does not exist: {MetaPath}", options.MetaPath);
                return -1;
            }

            // 创建输出目录
            if (!Directory.Exists(options.OutputPath))
            {
                Directory.CreateDirectory(options.OutputPath);
                _logger.LogInformation("📁 Created output directory: {OutputPath}", options.OutputPath);
            }

            // Read meta database to get file path and key mappings
            var fileKeyMap = ReadMetaDatabaseAsync(options.MetaPath, options.DatabaseKey, options.Region);
            _logger.LogInformation("🔑 Loaded {Count} file-key mappings from meta database", fileKeyMap.Count);

            // Traverse dat folder and decrypt files
            int processedCount = 0;
            int successCount = 0;
            int errorCount = 0;
            int skippedCount = 0;

            await ProcessDatDirectoryAsync(options.InputPath, options.OutputPath, fileKeyMap, options, skipExisting,
                (processed, success, error, skipped) => 
                {
                    processedCount = processed;
                    successCount = success;
                    errorCount = error;
                    skippedCount = skipped;
                });

            _logger.LogInformation("🎉 Decryption completed!");
            _logger.LogInformation("📊 Total processed: {ProcessedCount}", processedCount);
            _logger.LogInformation("✅ Successful: {SuccessCount}", successCount);
            if (skippedCount > 0)
            {
                _logger.LogInformation("⏭️ Skipped: {SkippedCount}", skippedCount);
            }
            _logger.LogInformation("❌ Errors: {ErrorCount}", errorCount);

            return errorCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to decrypt dat files");
            return -1;
        }
    }

    /// <summary>
    /// Read meta database, get file path (h column) to key (e column) mappings
    /// </summary>
    private Dictionary<string, long> ReadMetaDatabaseAsync(string metaPath, string? databaseKey, Core.Region region)
    {
        var fileKeyMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // 检查文件是否是加密的数据库（原始文件）还是解密后的数据库
        bool isEncrypted = IsEncryptedDatabase(metaPath);
        
        if (isEncrypted)
        {
            // 处理加密的数据库文件
            ReadEncryptedDatabase(metaPath, databaseKey, region, fileKeyMap);
        }
        else
        {
            // 处理解密后的标准 SQLite 数据库文件
            ReadDecryptedDatabase(metaPath, fileKeyMap);
        }

        return fileKeyMap;
    }

    /// <summary>
    /// Check if database file is encrypted
    /// </summary>
    private bool IsEncryptedDatabase(string filePath)
    {
        try
        {
            // 尝试用标准 SQLite 打开文件
            var connectionString = $"Data Source={filePath}";
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            connection.Open();
            
            // 尝试查询 sqlite_master 表
            using var command = new Microsoft.Data.Sqlite.SqliteCommand("SELECT name FROM sqlite_master LIMIT 1", connection);
            var result = command.ExecuteScalar();
            
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Read encrypted database
    /// </summary>
    private void ReadEncryptedDatabase(string metaPath, string? databaseKey, Core.Region region, Dictionary<string, long> fileKeyMap)
    {
        // Determine database key
        byte[] keyBytes;
        if (!string.IsNullOrEmpty(databaseKey))
        {
            keyBytes = ParseHexKey(databaseKey);
        }
        else
        {
            keyBytes = _keyManager.GetDatabaseDecryptionKey(region);
        }

        // Open encrypted database
        IntPtr db = IntPtr.Zero;
        try
        {
            db = Sqlite3MC.Open(metaPath);
            Sqlite3MC.Key_SetBytes(db, keyBytes);

            // Test database connection
            if (!Sqlite3MC.ValidateReadable(db, out string? errorMsg))
            {
                throw new InvalidOperationException($"Failed to decrypt meta database. Check your key. Error: {errorMsg}");
            }

            _logger.LogInformation("✅ Successfully opened encrypted meta database");

            ReadDatabaseContent(db, fileKeyMap);
        }
        finally
        {
            if (db != IntPtr.Zero)
            {
                Sqlite3MC.Close(db);
            }
        }
    }

    /// <summary>
    /// Read decrypted standard database
    /// </summary>
    private void ReadDecryptedDatabase(string metaPath, Dictionary<string, long> fileKeyMap)
    {
        var connectionString = $"Data Source={metaPath}";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();

        _logger.LogInformation("✅ Successfully opened decrypted meta database");

        // Query a table, get h(url) and e(key) columns
        const string query = "SELECT h, e FROM a WHERE h IS NOT NULL AND e IS NOT NULL AND h != '' AND e != ''";
        
        using var command = new Microsoft.Data.Sqlite.SqliteCommand(query, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            try
            {
                string? url = reader["h"]?.ToString();     // h 列
                string? keyStr = reader["e"]?.ToString();   // e 列

                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(keyStr))
                {
                    // 尝试解析密钥为 long
                    if (long.TryParse(keyStr, out long key))
                    {
                        fileKeyMap[url] = key;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to parse key for file {Url}: {Key}", url, keyStr);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading row from meta database");
            }
        }
    }

    /// <summary>
    /// Read database content (using sqlite3mc)
    /// </summary>
    private void ReadDatabaseContent(IntPtr db, Dictionary<string, long> fileKeyMap)
    {
        // Query a table, get h(url) and e(key) columns
        const string query = "SELECT h, e FROM a WHERE h IS NOT NULL AND e IS NOT NULL AND h != '' AND e != ''";
        
        Sqlite3MC.ForEachRow(query, db, (stmt) =>
        {
            try
            {
                string? url = Sqlite3MC.ColumnText(stmt, 0);     // h 列
                string? keyStr = Sqlite3MC.ColumnText(stmt, 1);   // e 列

                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(keyStr))
                {
                    // 尝试解析密钥为 long
                    if (long.TryParse(keyStr, out long key))
                    {
                        fileKeyMap[url] = key;
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to parse key for file {Url}: {Key}", url, keyStr);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading row from meta database");
            }
        });
    }

    /// <summary>
    /// Recursively process all files in input directory (parallel processing)
    /// </summary>
    private async Task ProcessDatDirectoryAsync(string inputDir, string outputDir, 
        Dictionary<string, long> fileKeyMap, DecryptDatOptions options, bool skipExisting, Action<int, int, int, int> progressCallback)
    {
        // Thread-safe counters
        int processedCount = 0;
        int successCount = 0;
        int errorCount = 0;
        int skippedCount = 0;
        var lockObj = new object();

        // Recursively traverse all files (no directory structure restrictions)
        var allFiles = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories);
        
        _logger.LogInformation("📁 Found {FileCount} files to process", allFiles.Length);
        
        if (allFiles.Length == 0)
        {
            _logger.LogWarning("⚠️ No files found in input directory: {InputDir}", inputDir);
            progressCallback(0, 0, 0, 0);
            return;
        }

        // Configure parallel options
        int maxThreads = options.MaxThreads ?? Environment.ProcessorCount;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(maxThreads, Environment.ProcessorCount * 2)), // 限制线程数范围
            CancellationToken = CancellationToken.None
        };

        _logger.LogInformation("🚀 Starting parallel decryption with {ThreadCount} threads", parallelOptions.MaxDegreeOfParallelism);

        // Progress reporting task
        var progressReportingTask = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(2000); // 每2秒报告一次进度
                
                int currentProcessed, currentSuccess, currentError, currentSkipped;
                lock (lockObj)
                {
                    currentProcessed = processedCount;
                    currentSuccess = successCount;
                    currentError = errorCount;
                    currentSkipped = skippedCount;
                }
                
                if (currentProcessed >= allFiles.Length)
                    break;
                
                progressCallback(currentProcessed, currentSuccess, currentError, currentSkipped);
                
                if (currentSkipped > 0)
                {
                    _logger.LogInformation("📊 Progress: {ProcessedCount}/{TotalCount} files processed (✅{SuccessCount} ⏭️{SkippedCount} ❌{ErrorCount})", 
                        currentProcessed, allFiles.Length, currentSuccess, currentSkipped, currentError);
                }
                else
                {
                    _logger.LogInformation("📊 Progress: {ProcessedCount}/{TotalCount} files processed (✅{SuccessCount} ❌{ErrorCount})", 
                        currentProcessed, allFiles.Length, currentSuccess, currentError);
                }
            }
        });

        // Parallel processing of all files
        await Task.Run(() =>
        {
            Parallel.ForEach(allFiles, parallelOptions, filePath =>
            {
                int localProcessed = 0, localSuccess = 0, localError = 0, localSkipped = 0;
                
                try
                {
                    // 获取文件名（用于与数据库记录匹配）
                    string fileName = Path.GetFileName(filePath);
                    
                    // 获取相对路径（用于保持目录结构）
                    string relativePath = Path.GetRelativePath(inputDir, filePath);
                    
                    // 构造输出路径（保持相同的目录结构）
                    string outputFilePath = Path.Combine(outputDir, relativePath);
                    
                    // Check if need to skip existing files
                    if (skipExisting && File.Exists(outputFilePath))
                    {
                        localSkipped = 1;
                        localProcessed = 1;
                        
                        // 在详细模式下显示跳过的文件
                        if (options.Verbose)
                        {
                            _logger.LogDebug("⏭️ Skipping existing file: {RelativePath}", relativePath);
                        }
                    }
                    else
                    {
                        // Find corresponding decryption key
                        if (!fileKeyMap.TryGetValue(fileName, out long key))
                        {
                            _logger.LogWarning("⚠️ No decryption key found for file: {FileName} (path: {RelativePath})", 
                                fileName, relativePath);
                            localError = 1;
                        }
                        else
                        {
                            string? outputDirPath = Path.GetDirectoryName(outputFilePath);
                            
                            // Ensure output directory exists (thread-safe)
                            if (!string.IsNullOrEmpty(outputDirPath))
                            {
                                lock (lockObj)
                                {
                                    if (!Directory.Exists(outputDirPath))
                                    {
                                        Directory.CreateDirectory(outputDirPath);
                                    }
                                }
                            }

                            // Decrypt file
                            AssetBundleDecryptor.DecryptFileToFile(filePath, outputFilePath, key);
                            
                            localSuccess = 1;
                        }
                        
                        localProcessed = 1;
                    }
                }
                catch (Exception ex)
                {
                    localError = 1;
                    localProcessed = 1;
                    string relativePath = Path.GetRelativePath(inputDir, filePath);
                    _logger.LogError(ex, "❌ Failed to decrypt file: {RelativePath}", relativePath);
                }
                
                // 线程安全地更新计数器
                lock (lockObj)
                {
                    processedCount += localProcessed;
                    successCount += localSuccess;
                    errorCount += localError;
                    skippedCount += localSkipped;
                    
                    // Display first few successful files
                    if (localSuccess == 1 && successCount <= 5)
                    {
                        string relativePath = Path.GetRelativePath(inputDir, filePath);
                        _logger.LogInformation("🔓 Decrypted: {RelativePath}", relativePath);
                    }
                    
                    // Display first few skipped files
                    if (localSkipped == 1 && skippedCount <= 3)
                    {
                        string relativePath = Path.GetRelativePath(inputDir, filePath);
                        _logger.LogInformation("⏭️ Skipped existing: {RelativePath}", relativePath);
                    }
                }
            });
        });

        // Stop progress reporting task
        await progressReportingTask;

        // Final report
        progressCallback(processedCount, successCount, errorCount, skippedCount);
        
        _logger.LogInformation("🎉 Parallel decryption completed!");
        
        if (skippedCount > 0)
        {
            _logger.LogInformation("📊 Final stats: {ProcessedCount} processed, ✅{SuccessCount} success, ⏭️{SkippedCount} skipped, ❌{ErrorCount} errors", 
                processedCount, successCount, skippedCount, errorCount);
        }
        else
        {
            _logger.LogInformation("📊 Final stats: {ProcessedCount} processed, ✅{SuccessCount} success, ❌{ErrorCount} errors", 
                processedCount, successCount, errorCount);
        }
    }

    /// <summary>
    /// Parse hexadecimal key string
    /// </summary>
    private byte[] ParseHexKey(string hexKey)
    {
        try
        {
            // 移除可能的前缀和空格
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
            throw;
        }
    }
}