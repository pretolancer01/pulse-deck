using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualBasic.FileIO;

namespace PulseDeck.Services;
public sealed record TempRow(string Name, long Bytes, DateTime ModifiedAt, string Folder, string FullName)
{
    public string Size => TempService.FormatSize(Bytes);
    public string Modified => ModifiedAt.ToString("dd/MM/yyyy HH:mm");
}
public sealed record TempResult(List<TempRow> Rows, int Skipped);
public static class TempService
{
    public static string Root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    public static string FormatSize(double bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824:N1} GB" : bytes >= 1048576 ? $"{bytes / 1048576:N1} MB" : bytes >= 1024 ? $"{bytes / 1024:N0} KB" : $"{bytes:N0} B";
    public static TempResult Scan(CancellationToken cancellation, string? rootOverride = null)
    {
        string root = rootOverride ?? Root;
        var pending = new Stack<string>(); pending.Push(root);
        var rows = new List<TempRow>(); int skipped = 0;
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                        var file = new FileInfo(entry);
                        rows.Add(new(file.Name, file.Length, file.LastWriteTime, file.DirectoryName ?? "", file.FullName));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        return new(rows.OrderByDescending(x => x.ModifiedAt).ToList(), skipped);
    }
    public static void ValidateForRecycle(string fullName)
    {
        string full = Path.GetFullPath(fullName);
        if (!full.StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Arquivo fora da pasta temporária.");
        // Check every ancestor, including the temp root. A changed directory link must not redirect deletion.
        var file = new FileInfo(full);
        if (!file.Exists || (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new IOException("O item não é um arquivo comum.");
        for (var dir = file.Directory; dir != null; dir = dir.Parent)
        {
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("O caminho contém um link de diretório.");
        }
    }
    public static (int Removed, int Failed) Recycle(IEnumerable<TempRow> selected)
    {
        int removed = 0, failed = 0;
        foreach (var row in selected)
        {
            try
            {
                ValidateForRecycle(row.FullName);
                FileSystem.DeleteFile(row.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { failed++; }
        }
        return (removed, failed);
    }
}
