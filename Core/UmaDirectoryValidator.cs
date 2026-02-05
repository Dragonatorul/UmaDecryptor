using Microsoft.Extensions.Logging;

namespace UmaDecryptor.Core;

/// <summary>
/// UMA directory structure validator
/// </summary>
public class UmaDirectoryValidator
{
    private readonly ILogger<UmaDirectoryValidator> _logger;

    public UmaDirectoryValidator(ILogger<UmaDirectoryValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Asynchronously validate UMA directory structure
    /// </summary>
    public async Task<bool> ValidateAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogError("Directory does not exist: {Path}", path);
            return false;
        }

        var validationTasks = new List<Task<bool>>();
        
        // Validate meta file (note: meta is a file, not a folder)
        validationTasks.Add(ValidateMetaFileAsync(path));
        
        // Validate folders
        var requiredFolders = new[] { "master", "dat" };
        validationTasks.AddRange(requiredFolders.Select(folder => ValidateFolderAsync(path, folder)));
        
        var results = await Task.WhenAll(validationTasks);
        var allValid = results.All(r => r);

        if (allValid)
        {
            _logger.LogInformation("UMA directory structure validation passed");
        }
        else
        {
            _logger.LogError("UMA directory structure validation failed");
        }

        return allValid;
    }

    /// <summary>
    /// Validate meta file
    /// </summary>
    private async Task<bool> ValidateMetaFileAsync(string basePath)
    {
        var metaPath = Path.Combine(basePath, "meta");
        
        return await Task.Run(() =>
        {
            if (!File.Exists(metaPath))
            {
                _logger.LogError("Required meta file missing: {MetaPath}", metaPath);
                return false;
            }

            var fileInfo = new FileInfo(metaPath);
            if (fileInfo.Length == 0)
            {
                _logger.LogError("Meta file is empty: {MetaPath}", metaPath);
                return false;
            }

            _logger.LogInformation("Meta file validation passed: {FileSize:N0} bytes", fileInfo.Length);
            return true;
        });
    }

    /// <summary>
    /// Validate single folder
    /// </summary>
    private async Task<bool> ValidateFolderAsync(string basePath, string folderName)
    {
        var folderPath = Path.Combine(basePath, folderName);
        
        return await Task.Run(() =>
        {
            if (!Directory.Exists(folderPath))
            {
                _logger.LogError("Required folder missing: {FolderName}", folderName);
                return false;
            }

            var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                _logger.LogWarning("Folder is empty: {FolderName}", folderName);
                // Empty folder is not an error, just a warning
            }

            _logger.LogDebug("Folder validation passed: {FolderName} ({FileCount} files)", 
                folderName, files.Length);
            return true;
        });
    }
}