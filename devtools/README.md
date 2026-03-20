# Log Message Semantic Clustering

A Python tool that uses NLP (sentence transformers) to cluster log messages by semantic similarity rather than exact string matching. Perfect for analyzing large log files and identifying recurring issues across TeleTap components (Wintap, Mactap, Lintap).

## Features

- **Semantic grouping**: Groups messages by meaning, not just word matching
- **Log level filtering**: Focus on specific log levels (Error, Warn, Info) or all messages
- **Automatic normalization**: Strips out numbers, paths, and type names
- **Statistical summary**: Shows cluster size, percentage of total logs, and compression ratio
- **Adjustable clustering**: Control granularity with distance threshold parameter

## Installation

```bash
# Create a virtual environment (recommended)
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt
```

## Usage

### Basic Usage

```bash
# Analyze errors only (default)
python log_cluster.py wintap.log

# Analyze warnings
python log_cluster.py wintap.log 0.7 Warn

# Analyze info messages
python log_cluster.py wintap.log 0.7 Info

# Analyze all log levels
python log_cluster.py wintap.log 0.7 all
```

### Advanced Usage

```bash
# Tighter clustering (more specific groups)
python log_cluster.py wintap.log 0.5 Error

# Looser clustering (broader groups)
python log_cluster.py wintap.log 0.9 Error

# Custom threshold with all messages
python log_cluster.py wintap.log 0.6 all
```

## Parameters

1. **log_file** (required): Path to your log file
2. **distance_threshold** (optional, default: 0.7): Controls clustering granularity
   - `0.5` - Tight clusters (very similar messages only)
   - `0.7` - Balanced (recommended)
   - `0.9` - Loose clusters (broadly similar messages)
3. **log_level** (optional, default: Error): Filter by log level
   - `Error` - Error messages only (default)
   - `Warn` - Warning messages only
   - `Info` - Info messages only
   - `all` - No filtering, include all levels

## Example Output

```
Reading log file: wintap.log
Filtering to level: Error
Parsed 156 log messages
Found 23 unique message templates
Loading model: all-MiniLM-L6-v2
Generating embeddings...
Clustering with distance threshold: 0.7

================================================================================
SEMANTIC LOG MESSAGE CLUSTERS
================================================================================

Total clusters: 8
Total messages: 156
Compression ratio: 19.5:1

────────────────────────────────────────────────────────────────────────────────
Cluster 1/8 (Label: 0)
Total occurrences: 89 (57.1%)
Unique variants: 3
────────────────────────────────────────────────────────────────────────────────
    45 | Error creating TcpConnection object on pid: NUM,  exception: SYSTYPE
    32 | Error loading plugin assemblies: No service for type 'TYPE' has been registered.
    12 | Error reading ETLConfig from disk, using default values
```

## Log Format Support

The tool expects logs in this format:
```
MM/DD/YYYY HH:MM:SS [Level] [Source]: Message
```

Example:
```
11/13/2025 10:00:33 [Info]  [CacheManager..ctor]:   Cache Manager is starting up
11/13/2025 10:00:33 [Error] [PluginManager.LoadPluginAssemblies]:   Error loading plugin
```

## How It Works

1. **Parsing**: Extracts log messages and filters by level
2. **Normalization**: Replaces numbers, paths, and type names with placeholders
3. **Embedding**: Uses sentence transformers to convert messages to semantic vectors
4. **Clustering**: Groups semantically similar messages using hierarchical clustering
5. **Analysis**: Shows clusters ranked by frequency with statistics

## Tips

- Start with default settings (Error level, 0.7 threshold)
- If clusters are too broad, decrease threshold (try 0.5)
- If too many small clusters, increase threshold (try 0.9)
- Use `all` level filter to see patterns across all log types
- First run downloads the ML model (~80MB) - subsequent runs are faster

## Troubleshooting

**No messages found**: Check that your log level exists in the file. Try `all` to see everything.

**Out of memory**: Process logs in batches or use a machine with more RAM.

**Slow performance**: First run downloads the model. Subsequent runs are much faster.

## Requirements

- Python 3.8+
- ~1GB RAM for typical log files
- Internet connection (first run only, to download ML model)