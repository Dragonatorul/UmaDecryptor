using CommandLine;
using UmaDecryptor.Core;

namespace UmaDecryptor.Commands;

/// <summary>
/// decrypt-dat command options
/// </summary>
[Verb("decrypt-dat", HelpText = "Decrypt files in resource folder (supports arbitrary directory structure)")]
public class DecryptDatOptions
{
    [Option('i', "input", Required = true, HelpText = "Input path (folder containing files to decrypt, supports arbitrary directory structure)")]
    public string InputPath { get; set; } = string.Empty;

    [Option('o', "output", Required = true, HelpText = "Output path (decrypted folder path, maintains original directory structure)")]
    public string OutputPath { get; set; } = string.Empty;

    [Option('m', "meta", Required = true, HelpText = "Meta database file path (used to get mapping between filenames and decryption keys)")]
    public string MetaPath { get; set; } = string.Empty;

    [Option('k', "key", Required = false, HelpText = "Database decryption key (hexadecimal string, e.g.: AABBCCDD...)")]
    public string? DatabaseKey { get; set; }

    [Option('r', "region", Default = Region.Japan, HelpText = "Server region (Japan=0, Global=1, default: Japan)")]
    public Region Region { get; set; } = Region.Japan;

    [Option('t', "threads", Required = false, HelpText = "Number of parallel processing threads (default: CPU core count)")]
    public int? MaxThreads { get; set; }

    [Option("overwrite", Required = false, HelpText = "Overwrite existing files (full update mode)")]
    public bool Overwrite { get; set; } = false;

    [Option('v', "verbose", Required = false, HelpText = "Show detailed logs")]
    public bool Verbose { get; set; }
}