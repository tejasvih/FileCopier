namespace MediaDevices.Internal;

// Owns the native pagination contract shared by both Item.GetChildren overloads.
// A failed page or unreadable object must not look like a complete folder listing.
internal static class CheckedObjectEnumerator
{
    private const int PageSize = 32;
    private const int SFalse = 1;

    internal static IEnumerable<T> Enumerate<T>(
        int enumerationResult,
        IEnumPortableDeviceObjectIDs? enumerator,
        Func<string, T> readObject)
    {
        MediaDeviceException.ThrowIfComError(enumerationResult,
            nameof(IPortableDeviceContent), nameof(IPortableDeviceContent.EnumObjects));
        if (enumerator == null)
            throw new IOException("IPortableDeviceContent.EnumObjects returned no enumerator.");

        var objectIds = new string[PageSize];
        while (true)
        {
            // Failed drivers may leave the output count unchanged. Never reuse it.
            uint fetched = 0;
            Array.Clear(objectIds);
            var result = enumerator.Next(PageSize, objectIds, ref fetched);
            MediaDeviceException.ThrowIfComError(result,
                nameof(IEnumPortableDeviceObjectIDs), nameof(IEnumPortableDeviceObjectIDs.Next));
            if (fetched > objectIds.Length)
                throw new IOException("IEnumPortableDeviceObjectIDs.Next returned an invalid object count.");

            for (var index = 0; index < fetched; index++)
            {
                var objectId = objectIds[index];
                if (string.IsNullOrEmpty(objectId))
                    throw new IOException("IEnumPortableDeviceObjectIDs.Next returned an empty object ID.");

                // Deliberately propagate metadata failures, including FileNotFoundException.
                yield return readObject(objectId);
            }

            // Only S_FALSE confirms completion; an empty S_OK page is no proof of EOF.
            if (result == SFalse)
                yield break;
            if (fetched == 0)
                throw new IOException("IEnumPortableDeviceObjectIDs.Next made no progress without confirming completion.");
        }
    }
}
