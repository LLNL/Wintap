#!/usr/bin/env python3
"""
Semantic log message clustering using sentence transformers.
Groups log messages by semantic similarity rather than exact string matching.
"""

import re
import sys
from collections import Counter
from sentence_transformers import SentenceTransformer
from sklearn.cluster import AgglomerativeClustering
import numpy as np

def parse_log_line(line, level_filter=None):
    """
    Extract the log message from the line if it matches the level filter.
    
    Args:
        line: Log line to parse
        level_filter: Log level to filter (e.g., 'Error', 'Warn', 'Info'), case-insensitive
    
    Returns:
        Message string if level matches (or no filter), None otherwise
    """
    # Match pattern: timestamp [Level] [Source]: message
    pattern = r'\[(\w+)\]\s+\[.*?\]:\s+(.+)$'
    match = re.search(pattern, line)
    if not match:
        return None
    
    level, message = match.groups()
    
    # If no filter specified, return all messages
    if level_filter is None:
        return message.strip()
    
    # Case-insensitive level matching
    if level.lower() == level_filter.lower():
        return message.strip()
    
    return None

def normalize_message(msg):
    """Basic normalization to reduce noise"""
    # Replace numbers
    msg = re.sub(r'\b\d+\b', 'NUM', msg)
    # Replace file paths
    msg = re.sub(r'/[\w/\.\-]+', '/PATH', msg)
    msg = re.sub(r'\\[\w\\\.\-]+', r'\\PATH', msg)
    # Replace fully qualified type names
    msg = re.sub(r'\b(gov\.llnl\.wintap\.[\w\.]+)', 'TYPE', msg)
    msg = re.sub(r'\bSystem\.[\w\.]+', 'SYSTYPE', msg)
    return msg

def cluster_messages(log_file, distance_threshold=0.7, model_name='all-MiniLM-L6-v2', level_filter='Error'):
    """
    Cluster log messages by semantic similarity.
    
    Args:
        log_file: Path to log file
        distance_threshold: Lower = tighter clusters (0.5-0.9 typical)
        model_name: Sentence transformer model to use
        level_filter: Log level to filter (Error, Warn, Info, etc.) or None for all
    """
    # Read and parse log file
    print(f"Reading log file: {log_file}")
    if level_filter:
        print(f"Filtering to level: {level_filter}")
    else:
        print("Including all log levels")
    
    with open(log_file, 'r') as f:
        lines = f.readlines()
    
    # Parse and normalize messages with level filtering
    messages = []
    for line in lines:
        parsed = parse_log_line(line, level_filter)
        if parsed:
            messages.append(normalize_message(parsed))
    
    print(f"Parsed {len(messages)} log messages")
    
    if not messages:
        print(f"No messages found! (Check if '{level_filter}' level exists in logs)")
        return
    
    # Load sentence transformer model
    print(f"Loading model: {model_name}")
    model = SentenceTransformer(model_name)
    
    # Get unique messages and their counts
    message_counts = Counter(messages)
    unique_messages = list(message_counts.keys())
    print(f"Found {len(unique_messages)} unique message templates")
    
    # Generate embeddings
    print("Generating embeddings...")
    embeddings = model.encode(unique_messages, show_progress_bar=True)
    
    # Hierarchical clustering
    print(f"Clustering with distance threshold: {distance_threshold}")
    clustering = AgglomerativeClustering(
        n_clusters=None, 
        distance_threshold=distance_threshold,
        linkage='average'
    )
    labels = clustering.fit_predict(embeddings)
    
    # Group messages by cluster
    clusters = {}
    for msg, label in zip(unique_messages, labels):
        if label not in clusters:
            clusters[label] = []
        count = message_counts[msg]
        clusters[label].append((msg, count))
    
    # Sort clusters by total occurrence count
    sorted_clusters = sorted(
        clusters.items(), 
        key=lambda x: sum(count for _, count in x[1]), 
        reverse=True
    )
    
    # Display results
    print("\n" + "="*80)
    print("SEMANTIC LOG MESSAGE CLUSTERS")
    print("="*80)
    
    total_clusters = len(sorted_clusters)
    total_messages = sum(sum(count for _, count in msgs) for _, msgs in sorted_clusters)
    
    print(f"\nTotal clusters: {total_clusters}")
    print(f"Total messages: {total_messages}")
    print(f"Compression ratio: {len(messages) / len(sorted_clusters):.1f}:1\n")
    
    for cluster_id, (label, msgs) in enumerate(sorted_clusters, 1):
        cluster_total = sum(count for _, count in msgs)
        percentage = (cluster_total / total_messages) * 100
        
        print(f"\n{'─'*80}")
        print(f"Cluster {cluster_id}/{total_clusters} (Label: {label})")
        print(f"Total occurrences: {cluster_total} ({percentage:.1f}%)")
        print(f"Unique variants: {len(msgs)}")
        print(f"{'─'*80}")
        
        # Show top 5 variants in this cluster
        for msg, count in sorted(msgs, key=lambda x: x[1], reverse=True)[:5]:
            print(f"  {count:4d} | {msg}")
        
        if len(msgs) > 5:
            remaining = sum(count for _, count in sorted(msgs, key=lambda x: x[1])[:-5])
            print(f"  ... and {len(msgs) - 5} more variants ({remaining} occurrences)")

def main():
    if len(sys.argv) < 2:
        print("Usage: python log_cluster.py <log_file> [distance_threshold] [log_level]")
        print("\nExample: python log_cluster.py wintap.log 0.7 Error")
        print("         python log_cluster.py wintap.log 0.7 Warn")
        print("         python log_cluster.py wintap.log 0.7 all")
        print("\nDistance threshold: 0.5 (tight) to 0.9 (loose), default 0.7")
        print("Log level: Error (default), Warn, Info, or 'all' for no filtering")
        sys.exit(1)
    
    log_file = sys.argv[1]
    distance_threshold = float(sys.argv[2]) if len(sys.argv) > 2 else 0.7
    level_filter = sys.argv[3] if len(sys.argv) > 3 else 'Error'
    
    # Handle 'all' as no filter
    if level_filter.lower() == 'all':
        level_filter = None
    
    try:
        cluster_messages(log_file, distance_threshold, level_filter=level_filter)
    except FileNotFoundError:
        print(f"Error: Log file '{log_file}' not found")
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)

if __name__ == "__main__":
    main()
