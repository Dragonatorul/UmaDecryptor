# UmaDecryptor

UMA Horse Girl Game Data Decryption Tool - A fully functional .NET 8 console application that supports decrypting UMA game database files and resource files.

## 🎯 Features

### Core Capabilities
- **📋 Complete Database Decryption**: Decrypt meta database, preserving all table structures and data (including horse racing data, etc.)
- **🌍 Multi-Region Support**: Supports Japanese server and Global international server, automatically using corresponding decryption keys
- **🔓 Resource File Decryption**: Decrypt game resource files (AssetBundle, etc.), supports arbitrary directory structures
- **📁 Directory Batch Processing**: One-click processing of the entire game data directory
- **⚡ Incremental Updates**: Intelligently skip existing files, only process new content after game updates
- **🚀 Multi-threaded Parallel Processing**: Efficiently process large numbers of files, making full use of CPU resources
- **🛠️ Flexible Configuration**: Supports custom keys and detailed logging mode

### Three Major Commands

#### 1. `uma-dir` - One-Stop Directory Processing 🚀
Process the complete UMA game directory, automatically decrypt all types of data files.

```bash
# Process the entire game directory (incremental mode, skip existing files by default)
UmaDecryptor.exe uma-dir -i "C:\Users\User\AppData\LocalLow\Cygames\umamusume" -o "C:\UMA_Decrypted"

# Global international server decryption
UmaDecryptor.exe uma-dir -i "C:\Games\UMA_Global" -o "C:\Games\UMA_Global_Decrypted" -r Global

# Full mode (overwrite all existing files)
UmaDecryptor.exe uma-dir -i "C:\Games\UMA" -o "C:\Games\UMA_Decrypted" --overwrite

# Use custom database key and detailed logging
UmaDecryptor.exe uma-dir -i "C:\Games\UMA" -o "C:\Games\UMA_Decrypted" -k "AABBCCDD..." -v

# Only view directory information, no decryption
UmaDecryptor.exe uma-dir -i "C:\Games\UMA" --info

# Multi-threaded parallel processing (specify thread count)
UmaDecryptor.exe uma-dir -i "C:\Games\UMA" -o "C:\Games\UMA_Decrypted" -t 8
```

**Automatic Processing Flow:**
1. 📋 Decrypt meta database (read all tables, not just 'a' table)
2. 📁 Copy master folder (keep intact, support incremental)
3. 🔓 Decrypt dat folder (all resource files, maintain directory structure, intelligently skip existing)

#### 2. `decrypt-db` - Database Decryption 📊
Decrypt a single database file, output a complete SQLite database.

```bash
# Basic decryption (output without suffix)
UmaDecryptor.exe decrypt-db -i meta -o meta_decrypted

# Global international server decryption
UmaDecryptor.exe decrypt-db -i meta -o meta_global -r Global

# Use custom key
UmaDecryptor.exe decrypt-db -i meta -o meta_decrypted -k "9C2BAB97BCF8C0C4..."

# Detailed logging mode
UmaDecryptor.exe decrypt-db -i meta -o meta_decrypted -v
```

**New Features:**
- ✅ **Complete Database**: Read all tables, including all file encryption data
- ✅ **Table Structure Preservation**: Completely rebuild all table structures and indexes
- ✅ **Standard Output**: Generate standard SQLite files, can be opened with any SQLite tool
- ✅ **Multi-Region Support**: Supports Japanese and Global servers, use `-r` parameter to specify

#### 3. `decrypt-dat` - Resource File Decryption 🗂️
Decrypt resource folders, supports arbitrary directory structures, not limited to standard dat folder format.

```bash
# Decrypt standard dat folder (incremental mode, skip existing files by default)
UmaDecryptor.exe decrypt-dat -i "C:\Game\dat" -o "C:\Game\dat_decrypted" -m "meta_decrypted"

# Global international server resource decryption
UmaDecryptor.exe decrypt-dat -i "C:\Game\dat" -o "C:\Game\dat_decrypted" -m "meta_global" -r Global

# Full mode (overwrite all existing files)
UmaDecryptor.exe decrypt-dat -i "C:\Game\dat" -o "C:\Game\dat_decrypted" -m "meta_decrypted" --overwrite

# Decrypt arbitrary structured resource folders
UmaDecryptor.exe decrypt-dat -i "C:\CustomAssets" -o "C:\Output" -m "meta.db" -v

# Multi-threaded parallel processing
UmaDecryptor.exe decrypt-dat -i "C:\Game\dat" -o "C:\Game\dat_decrypted" -m "meta.db" -t 16 --overwrite
```

**Flexible Features:**
- 🗂️ **Arbitrary Directory Structure**: Not limited to `dat/XX/FILENAME` format
- 📝 **Smart Matching**: Automatically match keys based on filenames and database records
- 📂 **Structure Preservation**: Completely preserve original directory hierarchy
- 🔍 **Recursive Processing**: Automatically scan all subdirectories
- ⚡ **Incremental Updates**: By default skip existing files, improve processing efficiency

## 🆕 Update Log

### v1.2.0 (2025-11-12)
- 🌍 **Added Global Server Support**: Full support for international server data decryption
- 🔑 **Fixed Key Generation Logic**: Implemented DBBaseKey XOR operation same as UmaViewer
- 📋 **Added Region Parameter**: All commands support `-r/--region` option (Japan/Global)
- ✅ **Key Matching UmaViewer**: Use exactly the same keys and decryption algorithms
- 🧪 **Test Passed**: Global server meta database decryption verification successful

### v1.1.0+
#### ⚡ Smart Update Mode
- **Default Incremental**: Automatically detect and skip existing files, improve processing efficiency
- **Simplified Options**: Use intuitive `--overwrite` option to control update mode
- **Flexible Switching**: Switch between incremental and full modes at any time
- **Efficient Updates**: After game version updates, only process new files

#### 📊 Enhanced Progress Report
```
🚀 Starting dat files decryption
⏭️ Skip existing files: enabled (incremental mode)
📊 Progress: 1200/5000 files processed (✅800 ⏭️350 ❌50)
🎉 Decryption completed!
📊 Final stats: 5000 processed, ✅4500 success, ⏭️450 skipped, ❌50 errors
```

#### 🎯 Usage Mode Comparison
```bash
# Incremental mode (default) - skip existing files
UmaDecryptor.exe decrypt-dat -i input -o output -m meta

# Full mode - overwrite all files
UmaDecryptor.exe decrypt-dat -i input -o output -m meta --overwrite
```

#### 🚀 Performance Optimization
- **Multi-threaded Parallel**: Default use CPU core count, customizable thread count
- **Memory Optimization**: Stream processing of large files, reduce memory usage
- **Network Friendly**: Suitable for processing large game directories on network storage

## 📦 Quick Start

### Environment Requirements
- Windows x64 system
- .NET 8 Runtime (or SDK for development)
- sqlite3mc_x64.dll (included with project)

### Installation and Running
```bash
# Clone project
git clone <repository-url>
cd UmaDecryptor

# Build project
dotnet build

# View help
dotnet run -- --help

# Process game directory
dotnet run -- uma-dir -i "C:\Users\User\AppData\LocalLow\Cygames\umamusume" -o "C:\UMA_Decrypted" -v
```

## 📁 Output Structure Example

```
Output directory/
├── meta                    # Decrypted complete database (containing all tables)
├── master/                 # Copied intact
│   └── (all original files)
└── dat/                    # Decrypted resource files
    ├── 2A/
    │   └── 2A2A5FYOKMKLWC6IDBUTYVFBZ7GAD73K  # Decrypted keeping original filename
    ├── 3B/
    │   └── 3B3B6GZPLNMLXD7JECVUZWGCA8HBE84L
    └── ...                 # Maintain complete directory structure
```

## 🔧 Technical Details

### Decryption Algorithm
- **Database**: Use sqlite3mc to handle SQLCipher encrypted databases
- **Resource Files**:
  - First 256 bytes remain unchanged
  - From 256 bytes start using key cyclic XOR decryption
  - Support negative keys (little-endian processing)

### Project Architecture
```
UmaDecryptor/
├── Commands/              # CLI command definitions
├── Core/                  # Core validation components
├── Crypto/                # Encryption and decryption algorithms
├── Database/              # Database processing
├── Services/              # Business logic services
├── Program.cs             # Program entry point
├── sqlite3mc_x64.dll      # SQLite encryption extension
└── UmaDecryptor.csproj      # Project configuration
```

## 🚀 Release and Deployment

### Standalone Executable
```bash
# Publish as single-file application
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Output location
cd bin\Release\net8.0\win-x64\publish\
UmaDecryptor.exe --help
```

### Dependencies
- **CommandLineParser**: CLI parameter parsing
- **System.Data.SQLite**: SQLite database operations  
- **Microsoft.Extensions.Logging**: Structured logging
- **sqlite3mc_x64.dll**: SQLite encryption extension (automatically included)

## 📋 Usage Examples

### Typical Workflow
```bash
# 1. Process complete game directory (recommended) - incremental mode
UmaDecryptor.exe uma-dir -i "C:\Users\User\AppData\LocalLow\Cygames\umamusume" -o "C:\UMA_Full_Decrypted" -v

# 1a. First complete processing or force full update
UmaDecryptor.exe uma-dir -i "C:\Users\User\AppData\LocalLow\Cygames\umamusume" -o "C:\UMA_Full_Decrypted" --overwrite -v

# 2. Or step-by-step processing:

# 2a. First decrypt database
UmaDecryptor.exe decrypt-db -i "meta" -o "meta_readable" -v

# 2b. Then decrypt resource files (incremental mode)
UmaDecryptor.exe decrypt-dat -i "dat_folder" -o "dat_decrypted" -m "meta_readable" -v

# 2c. Force re-decrypt all resource files
UmaDecryptor.exe decrypt-dat -i "dat_folder" -o "dat_decrypted" -m "meta_readable" --overwrite -v
```

### Advanced Usage
```bash
# Use custom key (incremental mode)
UmaDecryptor.exe uma-dir -i "game_dir" -o "output" -k "9C2BAB97BCF8C0C4F1A9EA7881A213F6C9EBF9D8D4C6A8E43CE5A259BDE7E9FD"

# Use custom key (full mode)
UmaDecryptor.exe uma-dir -i "game_dir" -o "output" -k "9C2BAB97BCF8C0C4F1A9EA7881A213F6C9EBF9D8D4C6A8E43CE5A259BDE7E9FD" --overwrite

# Process non-standard directory structure (incremental mode)
UmaDecryptor.exe decrypt-dat -i "extracted_assets" -o "decrypted_bundles" -m "meta_db"

# Process non-standard directory structure (full mode)
UmaDecryptor.exe decrypt-dat -i "extracted_assets" -o "decrypted_bundles" -m "meta_db" --overwrite

# View directory information
UmaDecryptor.exe uma-dir -i "game_dir" --info

# High-performance parallel processing (full mode)
UmaDecryptor.exe decrypt-dat -i "large_assets" -o "output" -m "meta_db" -t 32 --overwrite
```

### 💡 Usage Suggestions
```bash
# First game decryption - recommend using full mode
UmaDecryptor.exe uma-dir -i "game_dir" -o "output" --overwrite

# After game update - use default incremental mode
UmaDecryptor.exe uma-dir -i "game_dir" -o "output"

# When encountering issues - force reprocessing
UmaDecryptor.exe uma-dir -i "game_dir" -o "output" --overwrite -v
```

## ⚠️ Important Notes

- **Purpose**: This tool is only for learning and research purposes
- **Compliance**: Please comply with relevant game terms of service and local laws and regulations
- **Data**: Ensure to backup original data files
- **Compatibility**: Currently only supports Windows x64 platform

## 📄 License

MIT License - See [LICENSE](LICENSE) file for details

## 🤝 Contribution

Welcome to submit Issues and Pull Requests!

### Development Environment Setup
```bash
# Clone repository
git clone https://github.com/RanKaeder/UmaDecryptor.git
cd UmaDecryptor

# Restore dependencies
dotnet restore

# Build project
dotnet build

# Run tests
dotnet run -- --help
```

### Project Architecture
```
UmaDecryptor/
├── Commands/              # CLI command definitions
│   ├── UmaDirOptions.cs      # uma-dir command options
│   ├── DecryptDbOptions.cs   # decrypt-db command options
│   └── DecryptDatOptions.cs  # decrypt-dat command options
├── Core/                  # Core validation components
│   └── UmaDirectoryValidator.cs
├── Crypto/                # Encryption and decryption algorithms
│   └── AssetBundleDecryptor.cs
├── Database/              # Database processing
│   ├── DatabaseDecryptor.cs
│   ├── UmaDatabaseKeyManager.cs
│   └── Sqlite3MC.cs
├── Services/              # Business logic services
│   ├── UmaDirService.cs      # uma-dir service
│   ├── DecryptDbService.cs   # decrypt-db service
│   └── DecryptDatService.cs  # decrypt-dat service
├── Program.cs             # Program entry point
├── sqlite3mc_x64.dll      # SQLite encryption extension
└── UmaDecryptor.csproj    # Project configuration
```

## ⚠️ Important Notes

- **Purpose**: This tool is only for learning and research purposes
- **Compliance**: Please comply with relevant game terms of service and local laws and regulations  
- **Data**: Ensure to backup original data files
- **Compatibility**: Currently only supports Windows x64 platform
- **Responsibility**: Users bear all consequences arising from using this tool

## 🔗 Related Links

- [GitHub Repository](https://github.com/RanKaeder/UmaDecryptor)
- [Issues](https://github.com/RanKaeder/UmaDecryptor/issues)
- [Releases](https://github.com/RanKaeder/UmaDecryptor/releases)