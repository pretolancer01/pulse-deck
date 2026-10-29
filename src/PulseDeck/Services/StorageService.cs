using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
        Task.Run(() => Scan(drive, progress, cancellation), cancellation);

    static StorageResult Scan(StorageDrive drive, IProgress<StorageProgress>? progress, CancellationToken cancellation)
    {
        var largest = new PriorityQueue<StorageEntry, long>();
        var pending = new Stack<string>(); pending.Push(drive.Root);
        long visited = 0; int folders = 0;
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            string current = pending.Pop(); folders++;
            try
            {
                foreach (string file in Directory.EnumerateFiles(current))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        AddTop(largest, new(info.Name, info.FullName, info.Length));
                        visited++;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    if (visited % 4096 == 0) progress?.Report(new(visited, folders));
                }
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new DirectoryInfo(directory);
                        if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0) pending.Push(info.FullName);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        cancellation.ThrowIfCancellationRequested();
        progress?.Report(new(visited, folders));
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
