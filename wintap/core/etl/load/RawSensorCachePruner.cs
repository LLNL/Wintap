/*
 * Copyright (c) 2026, Lawrence Livermore National Security, LLC.
 * Produced at the Lawrence Livermore National Laboratory.
 * All rights reserved.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace gov.llnl.wintap.core.etl.load
{
    internal enum RawSensorCacheDeleteOutcome
    {
        Deleted,
        AlreadyAbsent
    }

    internal sealed class RawSensorCacheFile
    {
        internal RawSensorCacheFile(string fullPath, long length, DateTime lastWriteTimeUtc)
        {
            FullPath = Path.GetFullPath(fullPath);
            Length = length;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        internal string FullPath { get; }
        internal long Length { get; }
        internal DateTime LastWriteTimeUtc { get; }
    }

    internal sealed class RawSensorCacheDeletionFailure
    {
        internal RawSensorCacheDeletionFailure(RawSensorCacheFile file, Exception exception)
        {
            File = file;
            Exception = exception;
        }

        internal RawSensorCacheFile File { get; }
        internal Exception Exception { get; }
    }

    internal sealed class RawSensorCachePruneResult
    {
        internal RawSensorCachePruneResult(
            long startingBytes,
            long retainedBytes,
            IReadOnlyList<RawSensorCacheFile> deletedFiles,
            long deletedBytes,
            int alreadyAbsentFileCount,
            long alreadyAbsentBytes,
            int protectedFileCount,
            long protectedBytes,
            IReadOnlyList<RawSensorCacheDeletionFailure> deletionFailures,
            IReadOnlyList<RawSensorCacheFile> survivingFiles,
            bool limitReached)
        {
            StartingBytes = startingBytes;
            RetainedBytes = retainedBytes;
            DeletedFiles = deletedFiles;
            DeletedBytes = deletedBytes;
            AlreadyAbsentFileCount = alreadyAbsentFileCount;
            AlreadyAbsentBytes = alreadyAbsentBytes;
            ProtectedFileCount = protectedFileCount;
            ProtectedBytes = protectedBytes;
            DeletionFailures = deletionFailures;
            SurvivingFiles = survivingFiles;
            LimitReached = limitReached;
        }

        internal long StartingBytes { get; }
        internal long RetainedBytes { get; }
        internal IReadOnlyList<RawSensorCacheFile> DeletedFiles { get; }
        internal int DeletedFileCount => DeletedFiles.Count;
        internal long DeletedBytes { get; }
        internal int AlreadyAbsentFileCount { get; }
        internal long AlreadyAbsentBytes { get; }
        internal int ProtectedFileCount { get; }
        internal long ProtectedBytes { get; }
        internal IReadOnlyList<RawSensorCacheDeletionFailure> DeletionFailures { get; }
        internal int DeletionFailureCount => DeletionFailures.Count;
        internal IReadOnlyList<RawSensorCacheFile> SurvivingFiles { get; }
        internal bool LimitReached { get; }
    }

    internal static class RawSensorCachePruner
    {
        internal static IReadOnlyList<RawSensorCacheFile> EnumerateFinalizedFiles(string rawSensorRoot)
        {
            if (!Directory.Exists(rawSensorRoot))
            {
                return Array.Empty<RawSensorCacheFile>();
            }

            List<RawSensorCacheFile> files = new List<RawSensorCacheFile>();
            foreach (string path in Directory.EnumerateFiles(rawSensorRoot, "*", SearchOption.AllDirectories))
            {
                if (!string.Equals(Path.GetExtension(path), ".parquet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    FileInfo file = new FileInfo(path);
                    // Capture every mutable metadata value before any prune deletion can run.
                    files.Add(new RawSensorCacheFile(file.FullName, file.Length, file.LastWriteTimeUtc));
                }
                catch (FileNotFoundException)
                {
                    // The file disappeared while the enumeration snapshot was being captured.
                }
                catch (DirectoryNotFoundException)
                {
                    // Its containing partition disappeared while the snapshot was being captured.
                }
            }

            return files;
        }

        internal static RawSensorCachePruneResult Prune(
            IReadOnlyCollection<RawSensorCacheFile> files,
            long maxCacheSizeBytes,
            DateTime utcNow,
            TimeSpan protectionWindow,
            Func<string, RawSensorCacheDeleteOutcome> deleteOperation)
        {
            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }
            if (maxCacheSizeBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCacheSizeBytes));
            }
            if (protectionWindow < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(protectionWindow));
            }
            if (deleteOperation == null)
            {
                throw new ArgumentNullException(nameof(deleteOperation));
            }

            List<RawSensorCacheFile> snapshot = files.ToList();
            long startingBytes = snapshot.Sum(file => file.Length);
            long retainedBytes = startingBytes;
            DateTime protectedWindowStartUtc = utcNow - protectionWindow;
            List<RawSensorCacheFile> protectedFiles = snapshot
                .Where(file => file.LastWriteTimeUtc >= protectedWindowStartUtc)
                .ToList();
            HashSet<RawSensorCacheFile> removedFiles = new HashSet<RawSensorCacheFile>();
            List<RawSensorCacheFile> deletedFiles = new List<RawSensorCacheFile>();
            List<RawSensorCacheDeletionFailure> deletionFailures = new List<RawSensorCacheDeletionFailure>();
            long deletedBytes = 0;
            int alreadyAbsentFileCount = 0;
            long alreadyAbsentBytes = 0;

            if (retainedBytes > maxCacheSizeBytes)
            {
                IEnumerable<RawSensorCacheFile> eligibleFiles = snapshot
                    .Where(file => file.LastWriteTimeUtc < protectedWindowStartUtc)
                    .OrderBy(file => file.LastWriteTimeUtc)
                    .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase);

                foreach (RawSensorCacheFile file in eligibleFiles)
                {
                    if (retainedBytes <= maxCacheSizeBytes)
                    {
                        break;
                    }

                    try
                    {
                        RawSensorCacheDeleteOutcome outcome = deleteOperation(file.FullPath);
                        if (outcome == RawSensorCacheDeleteOutcome.Deleted)
                        {
                            removedFiles.Add(file);
                            retainedBytes -= file.Length;
                            deletedFiles.Add(file);
                            deletedBytes += file.Length;
                        }
                        else if (outcome == RawSensorCacheDeleteOutcome.AlreadyAbsent)
                        {
                            removedFiles.Add(file);
                            retainedBytes -= file.Length;
                            alreadyAbsentFileCount++;
                            alreadyAbsentBytes += file.Length;
                        }
                        else
                        {
                            throw new InvalidOperationException("Unknown raw-sensor cache delete outcome: " + outcome);
                        }
                    }
                    catch (Exception ex)
                    {
                        deletionFailures.Add(new RawSensorCacheDeletionFailure(file, ex));
                    }
                }
            }

            List<RawSensorCacheFile> survivingFiles = snapshot
                .Where(file => !removedFiles.Contains(file))
                .ToList();

            return new RawSensorCachePruneResult(
                startingBytes,
                retainedBytes,
                deletedFiles,
                deletedBytes,
                alreadyAbsentFileCount,
                alreadyAbsentBytes,
                protectedFiles.Count,
                protectedFiles.Sum(file => file.Length),
                deletionFailures,
                survivingFiles,
                retainedBytes <= maxCacheSizeBytes);
        }
    }
}
