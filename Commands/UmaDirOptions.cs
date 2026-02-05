using CommandLine;
using UmaDecryptor.Core;

namespace UmaDecryptor.Commands;

[Verb("uma-dir", HelpText = "Process UMA game directory for data decryption")]
public class UmaDirOptions
{
    [Option('i', "input", Required = true, HelpText = "UMA game directory path (containing meta, master, dat folders)")]
    public string InputPath { get; set; } = string.Empty;

    [Option('o', "output", HelpText = "Output directory path (not needed when using --info)")]
    public string? OutputPath { get; set; }

    [Option('k', "key", HelpText = "Database decryption key (hexadecimal string, e.g.: AABBCCDD...)")]
    public string? DatabaseKey { get; set; }

    [Option('r', "region", Default = Region.Japan, HelpText = "Server region (Japan=0, Global=1, default: Japan)")]
    public Region Region { get; set; } = Region.Japan;

    [Option('t', "threads", HelpText = "Number of parallel processing threads for dat files (default: CPU core count)")]
    public int? MaxThreads { get; set; }

    [Option("info", HelpText = "Display directory information only")]
    public bool InfoOnly { get; set; } = false;

    [Option("overwrite", HelpText = "Overwrite existing files (full update mode)")]
    public bool Overwrite { get; set; } = false;

    [Option('v', "verbose", HelpText = "Enable verbose logging")]
    public bool Verbose { get; set; } = false;
}