namespace UmaDecryptor.Database;

/// <summary>
/// UMA database entry
/// </summary>
public class UmaDatabaseEntry
{
    /// <summary>File type (m column)</summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>File name (n column)</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>URL or path (h column)</summary>
    public string Url { get; set; } = string.Empty;
    
    /// <summary>Dependencies (d column)</summary>
    public string Dependencies { get; set; } = string.Empty;
    
    /// <summary>Checksum (c column, if exists)</summary>
    public string? Checksum { get; set; }
    
    /// <summary>Key (e column, if exists)</summary>
    public string? Key { get; set; }

    public override string ToString()
    {
        return $"[{Type}] {Name} -> {Url}";
    }
}