using gov.llnl.wintap.core.etl.load;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Wintap.Tests
{
    public class RawSensorCachePrunerTests
    {
        private static readonly DateTime NowUtc = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan ProtectionWindow = TimeSpan.FromMinutes(5);

        [Fact]
        [Trait("Category", "cpr-01")]
        public void BelowLimitDeletesNothingAndReturnsEveryFile()
        {
            RawSensorCacheFile[] files =
            {
                File("old.parquet", 40, NowUtc.AddHours(-1)),
                File("current.parquet", 60, NowUtc)
            };
            int deleteCalls = 0;

            RawSensorCachePruneResult result = Prune(files, 100, _ =>
            {
                deleteCalls++;
                return RawSensorCacheDeleteOutcome.Deleted;
            });

            Assert.Equal(0, deleteCalls);
            Assert.Equal(100, result.StartingBytes);
            Assert.Equal(100, result.RetainedBytes);
            Assert.Equal(0, result.DeletedFileCount);
            Assert.True(result.LimitReached);
            Assert.Equal(files, result.SurvivingFiles);
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void OverLimitDeletesOldestUntilExactlyAtLimit()
        {
            RawSensorCacheFile oldest = File("oldest.parquet", 50, NowUtc.AddHours(-3));
            RawSensorCacheFile next = File("next.parquet", 50, NowUtc.AddHours(-2));
            RawSensorCacheFile newest = File("newest.parquet", 50, NowUtc.AddHours(-1));
            List<string> deletedPaths = new List<string>();

            RawSensorCachePruneResult result = Prune(new[] { newest, next, oldest }, 100, path =>
            {
                deletedPaths.Add(path);
                return RawSensorCacheDeleteOutcome.Deleted;
            });

            Assert.Equal(new[] { oldest.FullPath }, deletedPaths);
            Assert.Equal(100, result.RetainedBytes);
            Assert.Equal(1, result.DeletedFileCount);
            Assert.Equal(50, result.DeletedBytes);
            Assert.True(result.LimitReached);
            Assert.DoesNotContain(oldest, result.SurvivingFiles);
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void LastWriteTimeTiesUseOrdinalIgnoreCasePathOrder()
        {
            DateTime timestamp = NowUtc.AddHours(-1);
            RawSensorCacheFile zFile = File("z.parquet", 40, timestamp);
            RawSensorCacheFile aFile = File("A.parquet", 40, timestamp);
            RawSensorCacheFile mFile = File("m.parquet", 40, timestamp);
            List<string> deletedPaths = new List<string>();

            RawSensorCachePruneResult result = Prune(new[] { zFile, mFile, aFile }, 40, path =>
            {
                deletedPaths.Add(path);
                return RawSensorCacheDeleteOutcome.Deleted;
            });

            Assert.Equal(new[] { aFile.FullPath, mFile.FullPath }, deletedPaths);
            Assert.Equal(new[] { zFile }, result.SurvivingFiles);
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void ProtectedFilesAreNeverDeletedAndRemainUploadable()
        {
            RawSensorCacheFile oldFile = File("old.parquet", 60, NowUtc.AddHours(-1));
            RawSensorCacheFile boundaryFile = File("boundary.parquet", 60, NowUtc - ProtectionWindow);
            List<string> deletedPaths = new List<string>();

            RawSensorCachePruneResult result = Prune(new[] { oldFile, boundaryFile }, 60, path =>
            {
                deletedPaths.Add(path);
                return RawSensorCacheDeleteOutcome.Deleted;
            });

            Assert.Equal(new[] { oldFile.FullPath }, deletedPaths);
            Assert.Contains(boundaryFile, result.SurvivingFiles);
            Assert.Equal(1, result.ProtectedFileCount);
            Assert.Equal(60, result.ProtectedBytes);
            Assert.True(result.LimitReached);
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void ProtectedBytesAboveLimitReportLimitNotReached()
        {
            RawSensorCacheFile oldFile = File("old.parquet", 20, NowUtc.AddHours(-1));
            RawSensorCacheFile protectedOne = File("protected-1.parquet", 70, NowUtc.AddMinutes(-1));
            RawSensorCacheFile protectedTwo = File("protected-2.parquet", 70, NowUtc);

            RawSensorCachePruneResult result = Prune(
                new[] { oldFile, protectedOne, protectedTwo },
                100,
                _ => RawSensorCacheDeleteOutcome.Deleted);

            Assert.False(result.LimitReached);
            Assert.Equal(140, result.RetainedBytes);
            Assert.Equal(2, result.ProtectedFileCount);
            Assert.Equal(140, result.ProtectedBytes);
            Assert.Equal(new[] { protectedOne, protectedTwo }, result.SurvivingFiles);
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void SuccessfulDeletionUsesCapturedLengthAfterPhysicalFileIsGone()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string path = Path.Combine(directory, "captured.parquet");
                System.IO.File.WriteAllBytes(path, new byte[75]);
                System.IO.File.SetLastWriteTimeUtc(path, NowUtc.AddHours(-1));
                RawSensorCacheFile candidate = RawSensorCachePruner.EnumerateFinalizedFiles(directory).Single();

                RawSensorCachePruneResult result = Prune(new[] { candidate }, 0, filePath =>
                {
                    System.IO.File.Delete(filePath);
                    return RawSensorCacheDeleteOutcome.Deleted;
                });

                Assert.False(System.IO.File.Exists(path));
                Assert.Equal(75, result.StartingBytes);
                Assert.Equal(0, result.RetainedBytes);
                Assert.Equal(1, result.DeletedFileCount);
                Assert.Equal(75, result.DeletedBytes);
                Assert.Equal(0, result.DeletionFailureCount);
                Assert.Empty(result.SurvivingFiles);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void AlreadyAbsentCandidateIsRemovedWithoutDeletionFailure()
        {
            RawSensorCacheFile absent = File("absent.parquet", 75, NowUtc.AddHours(-1));

            RawSensorCachePruneResult result = Prune(
                new[] { absent },
                0,
                _ => RawSensorCacheDeleteOutcome.AlreadyAbsent);

            Assert.Equal(0, result.RetainedBytes);
            Assert.Equal(0, result.DeletedFileCount);
            Assert.Equal(1, result.AlreadyAbsentFileCount);
            Assert.Equal(75, result.AlreadyAbsentBytes);
            Assert.Equal(0, result.DeletionFailureCount);
            Assert.Empty(result.SurvivingFiles);
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void DeletionExceptionRetainsFileAndContinuesToLaterCandidates()
        {
            RawSensorCacheFile failed = File("failed.parquet", 60, NowUtc.AddHours(-2));
            RawSensorCacheFile deleted = File("deleted.parquet", 60, NowUtc.AddHours(-1));
            List<string> attemptedPaths = new List<string>();

            RawSensorCachePruneResult result = Prune(new[] { deleted, failed }, 60, path =>
            {
                attemptedPaths.Add(path);
                if (string.Equals(path, failed.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("simulated failure");
                }
                return RawSensorCacheDeleteOutcome.Deleted;
            });

            Assert.Equal(new[] { failed.FullPath, deleted.FullPath }, attemptedPaths);
            Assert.Equal(60, result.RetainedBytes);
            Assert.Equal(1, result.DeletionFailureCount);
            Assert.Equal(failed, result.DeletionFailures.Single().File);
            Assert.Contains(failed, result.SurvivingFiles);
            Assert.DoesNotContain(deleted, result.SurvivingFiles);
            Assert.True(result.LimitReached);
        }

        [Fact]
        [Trait("Category", "cpr-01")]
        public void EnumerationIncludesOnlyFinalizedParquetFilesBelowRawSensorRoot()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string partition = Path.Combine(directory, "sensor", "date=2026-08-27");
                Directory.CreateDirectory(partition);
                string finalized = Path.Combine(partition, "final.PARQUET");
                System.IO.File.WriteAllBytes(finalized, new byte[3]);
                System.IO.File.WriteAllBytes(Path.Combine(partition, "pending.parquet.active"), new byte[4]);
                System.IO.File.WriteAllBytes(Path.Combine(partition, "input.active"), new byte[5]);
                System.IO.File.WriteAllBytes(Path.Combine(partition, "notes.txt"), new byte[6]);

                IReadOnlyList<RawSensorCacheFile> files = RawSensorCachePruner.EnumerateFinalizedFiles(directory);

                RawSensorCacheFile file = Assert.Single(files);
                Assert.Equal(Path.GetFullPath(finalized), file.FullPath);
                Assert.Equal(3, file.Length);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static RawSensorCachePruneResult Prune(
            IReadOnlyCollection<RawSensorCacheFile> files,
            long limit,
            Func<string, RawSensorCacheDeleteOutcome> deleteOperation)
        {
            return RawSensorCachePruner.Prune(files, limit, NowUtc, ProtectionWindow, deleteOperation);
        }

        private static RawSensorCacheFile File(string name, long length, DateTime lastWriteTimeUtc)
        {
            return new RawSensorCacheFile(Path.Combine(Path.GetTempPath(), "cpr-01", name), length, lastWriteTimeUtc);
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "Wintap.Tests", "cpr-01", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
