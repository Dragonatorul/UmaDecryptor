using CommandLine;
using UmaDecryptor.Core;

namespace UmaDecryptor.Commands;

[Verb("decrypt-db", HelpText = "Decrypt single UMA database file")]
public class DecryptDbOptions
{
    [Option('i', "input", Required = true, HelpText = "Input encrypted database file path")]
    public string InputPath { get; set; } = string.Empty;

    [Option('o', "output", Required = true, HelpText = "Output decrypted database file path")]
    public string OutputPath { get; set; } = string.Empty;

    [Option('k', "key", HelpText = "Custom decryption key (hexadecimal format, optional - default key used if not provided)")]
    public string? CustomKey { get; set; }

    [Option('r', "region", Default = Region.Japan, HelpText = "Server region (Japan=0, Global=1, default: Japan)")]
    public Region Region { get; set; } = Region.Japan;

    [Option('c', "cipher", Default = 3, HelpText = "Encryption index (default: 3)")]
    public int CipherIndex { get; set; } = 3;

    [Option('v', "verbose", HelpText = "Enable verbose logging")]
    public bool Verbose { get; set; } = false;
}