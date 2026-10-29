using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace PulseDeck.Services;

public sealed record StorageEntry(string Name, string Path, long Bytes);
public sealed record StorageDrive(string Name, string Root, long TotalBytes, long FreeBytes);
public sealed record StorageProgress(long FilesVisited, int FoldersVisited);
public sealed record StorageResult(StorageDrive Drive, IReadOnlyList<StorageEntry> Files, IReadOnlyList<StorageEntry> Applications);

public static class StorageService
{
    const int Limit = 60;

    public static IReadOnlyList<StorageDrive> GetDrives()
    {
        var drives = new List<StorageDrive>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
                    drives.Add(new(drive.Name, drive.RootDirectory.FullName, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return drives;
    }

    public static Task<StorageResult> ScanAsync(StorageDrive drive, IProgress<StorageProgress>? progress, CancellationToken cancellation) =>
        ScanParallelAsync(drive, progress, cancellation);

    static async Task<StorageResult> ScanParallelAsync(StorageDrive drive, IProgress<StorageProgress>? progress, CancellationToken cancellation)
    {
        var largest = new PriorityQueue<StorageEntry, long>();
        var largestLock = new object();
        var folders = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        folders.Writer.TryWrite(drive.Root);
        long outstanding = 1, visited = 0;
        int foldersVisited = 0;
        int workers = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        progress?.Report(new(0, 0));

        async Task WorkerAsync()
        {
            await foreach (string current in folders.Reader.ReadAllAsync(cancellation))
            {
                Interlocked.Increment(ref foldersVisited);
                try
                {
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(current))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            try
                            {
                                var info = new FileInfo(file);
                                if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                                {
                                    var entry = new StorageEntry(info.Name, info.FullName, info.Length);
                                    lock (largestLock) AddTop(largest, entry);
                                }
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                            long count = Interlocked.Increment(ref visited);
                            if (count % 2048 == 0) progress?.Report(new(count, Volatile.Read(ref foldersVisited)));
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }

                    try
                    {
                        foreach (string directory in Directory.EnumerateDirectories(current))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            try
                            {
                                var info = new DirectoryInfo(directory);
                                if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                                Interlocked.Increment(ref outstanding);
                                if (!folders.Writer.TryWrite(info.FullName)) Interlocked.Decrement(ref outstanding);
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                finally
                {
                    if (Interlocked.Decrement(ref outstanding) == 0) folders.Writer.TryComplete();
                }
            }
        }

        try { await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(WorkerAsync, cancellation))); }
        catch (OperationCanceledException) { folders.Writer.TryComplete(); throw; }
        cancellation.ThrowIfCancellationRequested();
        progress?.Report(new(Volatile.Read(ref visited), Volatile.Read(ref foldersVisited)));
        return new(drive, largest.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Bytes).ToArray(), ReadLargestApplications());
    }

    static void AddTop(PriorityQueue<StorageEntry, long> queue, StorageEntry entry)
    {
        if (entry.Bytes <= 0) return;
        if (queue.Count < Limit) { queue.Enqueue(entry, entry.Bytes); return; }
        if (queue.TryPeek(out _, out long smallest) && entry.Bytes > smallest)
        { queue.Dequeue(); queue.Enqueue(entry, entry.Bytes); }
    }

    static IReadOnlyList<StorageEntry> ReadLargestApplications()
    {
        var entries = new Dictionary<string, StorageEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (string subName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = uninstall.OpenSubKey(subName);
                        string? name = sub?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!long.TryParse(sub?.GetValue("EstimatedSize")?.ToString(), out long kilobytes) || kilobytes <= 0) continue;
                        string? install = sub?.GetValue("InstallLocation") as string;
                        string path = !string.IsNullOrWhiteSpace(install) ? install : subName;
                        long bytes = kilobytes > long.MaxValue / 1024 ? long.MaxValue : kilobytes * 1024;
                        entries[name.Trim()] = new(name.Trim(), path, bytes);
                    }
                    catch (System.Security.SecurityException) { }
                    catch (IOException) { }
                }
            }
            catch (System.Security.SecurityException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return entries.Values.OrderByDescending(x => x.Bytes).Take(Limit).ToArray();
    }
}
