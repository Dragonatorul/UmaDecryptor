using System;
using System.IO;

namespace UmaDecryptor.Crypto;

/// <summary>
/// UMA game asset bundle decryptor
/// </summary>
public static class AssetBundleDecryptor
{
    // Default values (provided previously)
    private static readonly byte[] DefaultBaseKeys = new byte[]
    {
        0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB
    };
    private const long DefaultKey = -7673907454518172050L;

    /// <summary>
    /// Use default baseKeys + key to decrypt file and return decrypted byte[] (read all at once into memory).
    /// First 256 bytes remain unchanged (no XOR), from offset 256 start doing data[i] ^= keys[i % keys.Length] for each byte.
    /// </summary>
    /// <param name="inputFilePath">Input file path to decrypt</param>
    /// <param name="key">Decryption key</param>
    /// <returns>Decrypted byte array (can be directly passed to AssetBundle.LoadFromMemory)</returns>
    public static byte[] DecryptFileToBytes(string inputFilePath, long key)
    {
        return DecryptFileToBytes(inputFilePath, DefaultBaseKeys, key);
    }

    /// <summary>
    /// General interface: use specified baseKeys and key to decrypt file and return decrypted byte[].
    /// </summary>
    /// <param name="inputFilePath">Input file path (must exist)</param>
    /// <param name="baseKeys">baseKeys array (each element is a byte, function generates 8 bytes for each baseKeys element)</param>
    /// <param name="key">int64 key (supports negative numbers); converted to 8 bytes little-endian two's-complement then XOR with baseKeys to construct flat keys array)</param>
    /// <returns>Decrypted byte array</returns>
    public static byte[] DecryptFileToBytes(string inputFilePath, byte[] baseKeys, long key)
    {
        if (string.IsNullOrEmpty(inputFilePath))
            throw new ArgumentNullException(nameof(inputFilePath));
        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException("Input file not found", inputFilePath);
        if (baseKeys == null || baseKeys.Length == 0)
            throw new ArgumentException("baseKeys must not be null or empty", nameof(baseKeys));

        // Read entire file into memory (user requested no chunking)
        byte[] data = File.ReadAllBytes(inputFilePath);

        // Construct keyBytes (8 bytes little-endian). Ensure little-endian.
        byte[] keyBytes = BitConverter.GetBytes(key);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(keyBytes);

        // Construct flat keys: for each byte in baseKeys generate 8 bytes: base ^ keyBytes[j]
        int baseLen = baseKeys.Length;
        int keysLen = baseLen * 8;
        byte[] keys = new byte[keysLen];
        for (int i = 0; i < baseLen; ++i)
        {
            byte b = baseKeys[i];
            int baseOffset = i << 3; // i * 8
            for (int j = 0; j < 8; ++j)
            {
                keys[baseOffset + j] = (byte)(b ^ keyBytes[j]);
            }
        }

        // If file length <= 256, no bytes are XORed, return original data directly
        if (data.Length <= 256)
            return data;

        // From offset 256, perform cyclic XOR on each byte with keys
        for (int i = 256; i < data.Length; ++i)
        {
            data[i] ^= keys[i % keysLen];
        }

        return data;
    }

    /// <summary>
    /// Decrypt file and save to specified path
    /// </summary>
    /// <param name="inputFilePath">Input file path</param>
    /// <param name="outputFilePath">Output file path</param>
    /// <param name="key">Decryption key</param>
    public static void DecryptFileToFile(string inputFilePath, string outputFilePath, long key)
    {
        DecryptFileToFile(inputFilePath, outputFilePath, DefaultBaseKeys, key);
    }

    /// <summary>
    /// Decrypt file and save to specified path (general interface)
    /// </summary>
    /// <param name="inputFilePath">Input file path</param>
    /// <param name="outputFilePath">Output file path</param>
    /// <param name="baseKeys">Base key array</param>
    /// <param name="key">Decryption key</param>
    public static void DecryptFileToFile(string inputFilePath, string outputFilePath, byte[] baseKeys, long key)
    {
        byte[] decryptedData = DecryptFileToBytes(inputFilePath, baseKeys, key);
        
        // Ensure output directory exists
        string? outputDir = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }
        
        File.WriteAllBytes(outputFilePath, decryptedData);
    }
}