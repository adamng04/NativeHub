using System.Runtime.InteropServices;
using System.Text;
using NativeHub.Models;

namespace NativeHub.Services;

public enum EverythingSort : uint
{
    NameAscending = 1,
    NameDescending = 2,
    SizeDescending = 6,
    DateModifiedDescending = 14,
}

public sealed class EverythingSearchException(string message, uint errorCode) : Exception(message)
{
    public uint ErrorCode { get; } = errorCode;
}

public sealed class FileSearchService
{
    private const int MaximumResults = 500;
    private const uint RequestFileName = 0x1;
    private const uint RequestPath = 0x2;
    private const uint RequestSize = 0x10;
    private const uint RequestDateModified = 0x40;
    private readonly SemaphoreSlim _queryGate = new(1, 1);

    public bool IsEverythingAvailable
    {
        get
        {
            try { return Everything_GetMajorVersion() > 0; }
            catch (DllNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    public async Task<IReadOnlyList<FileSearchResult>> SearchAsync(
        string query,
        bool matchCase,
        bool regex,
        bool matchPath,
        bool wholeWord,
        EverythingSort sort,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        await _queryGate.WaitAsync(token);
        try
        {
            return await Task.Run<IReadOnlyList<FileSearchResult>>(() => SearchCore(
                query, matchCase, regex, matchPath, wholeWord, sort, token), token);
        }
        finally { _queryGate.Release(); }
    }

    private static List<FileSearchResult> SearchCore(
        string query,
        bool matchCase,
        bool regex,
        bool matchPath,
        bool wholeWord,
        EverythingSort sort,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Everything_GetMajorVersion() == 0)
            throw new EverythingSearchException("Everything is not running or IPC is disabled.", Everything_GetLastError());

        Everything_Reset();
        Everything_SetSearchW(query);
        Everything_SetMatchCase(matchCase);
        Everything_SetRegex(regex);
        Everything_SetMatchPath(matchPath);
        Everything_SetMatchWholeWord(wholeWord);
        Everything_SetSort((uint)sort);
        Everything_SetMax(MaximumResults);
        Everything_SetRequestFlags(RequestFileName | RequestPath | RequestSize | RequestDateModified);
        if (!Everything_QueryW(true))
        {
            var error = Everything_GetLastError();
            throw new EverythingSearchException($"Everything query failed (error {error}).", error);
        }

        token.ThrowIfCancellationRequested();
        var results = new List<FileSearchResult>();
        var buffer = new StringBuilder(32768);
        var count = Math.Min(Everything_GetNumResults(), (uint)MaximumResults);
        for (uint index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            buffer.Clear();
            _ = Everything_GetResultFullPathNameW(index, buffer, (uint)buffer.Capacity);
            var path = buffer.ToString();
            if (path.Length == 0) continue;
            var isFolder = Everything_IsFolderResult(index);
            var size = !isFolder && Everything_GetResultSize(index, out var rawSize) ? Math.Max(0, rawSize) : 0;
            var modified = Everything_GetResultDateModified(index, out var fileTime) && fileTime > 0
                ? DateTimeOffset.FromFileTime(fileTime)
                : DateTimeOffset.MinValue;
            results.Add(new(Path.GetFileName(path), path, size, modified, isFolder));
        }
        return results;
    }

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)] private static extern void Everything_SetSearchW(string search);
    [DllImport("Everything64.dll")] private static extern void Everything_SetMatchCase([MarshalAs(UnmanagedType.Bool)] bool value);
    [DllImport("Everything64.dll")] private static extern void Everything_SetRegex([MarshalAs(UnmanagedType.Bool)] bool value);
    [DllImport("Everything64.dll")] private static extern void Everything_SetMatchPath([MarshalAs(UnmanagedType.Bool)] bool value);
    [DllImport("Everything64.dll")] private static extern void Everything_SetMatchWholeWord([MarshalAs(UnmanagedType.Bool)] bool value);
    [DllImport("Everything64.dll")] private static extern void Everything_SetSort(uint value);
    [DllImport("Everything64.dll")] private static extern void Everything_SetMax(uint value);
    [DllImport("Everything64.dll")] private static extern void Everything_SetRequestFlags(uint value);
    [DllImport("Everything64.dll")] private static extern void Everything_Reset();
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_QueryW([MarshalAs(UnmanagedType.Bool)] bool wait);
    [DllImport("Everything64.dll")] private static extern uint Everything_GetNumResults();
    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)] private static extern uint Everything_GetResultFullPathNameW(uint index, StringBuilder buffer, uint maxCount);
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_GetResultSize(uint index, out long size);
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_GetResultDateModified(uint index, out long fileTime);
    [DllImport("Everything64.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Everything_IsFolderResult(uint index);
    [DllImport("Everything64.dll")] private static extern uint Everything_GetMajorVersion();
    [DllImport("Everything64.dll")] private static extern uint Everything_GetLastError();
}
