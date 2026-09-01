using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using AJCC.Core.Models;
using AJCC.Core.Parsers;

namespace AJCC.Core.Services;

public static class AjStateUpdater
{
    public static void Apply(AjState state, ModifiedParseResult result)
    {
        foreach (long removedId in result.RemovedIds)
        {
            RemoveById(state.Downloads, removedId);
            RemoveById(state.Uploads, removedId);
            RemoveById(state.Users, removedId);
            RemoveById(state.Servers, removedId);
            RemoveById(state.Searches, removedId);
        }

        foreach (AjDownload item in result.Downloads)
            Upsert(state.Downloads, item, item.Id);

        foreach (AjUpload item in result.Uploads)
        {
            AjUpload? existingUpload = state.Uploads.FirstOrDefault(upload => upload.Id == item.Id);
            if (existingUpload is not null && !IsUsableUploadFilename(item.Filename) && IsUsableUploadFilename(existingUpload.Filename))
                item.Filename = existingUpload.Filename;

            if (!IsUsableUploadFilename(item.Filename))
            {
                string? downloadFilename = GetDownloadFilenameFallback(state, item.ShareId);
                if (!string.IsNullOrWhiteSpace(downloadFilename))
                    item.Filename = downloadFilename;
            }

            Upsert(state.Uploads, item, item.Id);
        }

        foreach (AjUserSource item in result.Users)
            Upsert(state.Users, item, item.Id);

        foreach (AjServer item in result.Servers)
            Upsert(state.Servers, item, item.Id);

        foreach (AjSearch item in result.Searches)
            Upsert(state.Searches, item, item.Id);

        foreach (AjSearchEntry entry in result.SearchEntries)
            UpsertSearchEntry(state, entry);

        if (result.NetworkInfo is not null)
            state.NetworkInfo = result.NetworkInfo;

        if (result.Information is not null)
            state.Information = result.Information;
    }

    public static void RebuildShareFilenameLookup(AjState state)
    {
        state.ShareFilenameById.Clear();
    }

    public static bool IsUsableUploadFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return false;

        string name = GetFileNameOnly(filename.Trim());
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.StartsWith("ShareID ", StringComparison.OrdinalIgnoreCase))
            return false;

        if (name.EndsWith(".data", StringComparison.OrdinalIgnoreCase)
            && name[..^5].All(char.IsDigit))
            return false;

        return true;
    }

    private static string GetFileNameOnly(string value)
    {
        int slash = value.LastIndexOf('/');
        int backslash = value.LastIndexOf('\\');
        int index = slash > backslash ? slash : backslash;
        return index >= 0 && index + 1 < value.Length ? value[(index + 1)..] : value;
    }

    private static string? GetDownloadFilenameFallback(AjState state, long shareId)
    {
        if (shareId <= 0)
            return null;

        foreach (AjDownload download in state.Downloads.Where(download => download.ShareId == shareId))
        {
            string displayFilename = download.DisplayFilename.Trim();
            if (IsUsableUploadFilename(displayFilename))
                return displayFilename;

            string filename = download.Filename.Trim();
            if (IsUsableUploadFilename(filename))
                return filename;
        }

        return null;
    }

    private static void Upsert<T>(ObservableCollection<T> collection, T item, long id) where T : class
    {
        int existingIndex = -1;
        for (int index = 0; index < collection.Count; index++)
        {
            if (GetId(collection[index]) == id)
            {
                existingIndex = index;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            T existing = collection[existingIndex];

            if (item is AjUpload)
            {
                collection[existingIndex] = item;
                return;
            }

            CopyWritableProperties(item, existing);

            if (existing is AjSearch existingSearch && item is AjSearch updatedSearch)
            {
                foreach (AjSearchEntry entry in updatedSearch.Entries)
                    UpsertEntry(existingSearch, entry);
                existingSearch.NotifyEntriesChanged();
            }

            return;
        }

        collection.Add(item);
    }

    private static void UpsertSearchEntry(AjState state, AjSearchEntry entry)
    {
        if (entry.SearchId <= 0)
            return;

        AjSearch? search = state.Searches.FirstOrDefault(item => item.Id == entry.SearchId);
        if (search is null)
            return;

        UpsertEntry(search, entry);
    }

    private static void UpsertEntry(AjSearch search, AjSearchEntry entry)
    {
        AjSearchEntry? existing = search.Entries.FirstOrDefault(item => item.Id == entry.Id);
        if (existing is not null)
        {
            existing.Checksum = entry.Checksum;
            existing.Size = entry.Size;
            existing.Filename = entry.Filename;
            existing.SourceText = entry.SourceText;
            existing.FilenameUsers = entry.FilenameUsers;
            return;
        }

        search.Entries.Add(entry);
        search.NotifyEntriesChanged();
    }

    private static void CopyWritableProperties<T>(T source, T target) where T : class
    {
        Type type = typeof(T);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite)
                continue;

            if (property.GetIndexParameters().Length > 0)
                continue;

            if (type == typeof(AjDownload) && property.Name == nameof(AjDownload.IsRecentlyImported))
                continue;

            object? value = property.GetValue(source);
            property.SetValue(target, value);
        }
    }

    private static void RemoveById<T>(ObservableCollection<T> collection, long id) where T : class
    {
        T? found = collection.FirstOrDefault(x => GetId(x) == id);
        if (found is not null)
            collection.Remove(found);
    }

    private static long GetId<T>(T item)
    {
        return item?.GetType().GetProperty("Id")?.GetValue(item) is long id ? id : 0;
    }
}
