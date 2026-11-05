namespace FileCopier;

internal static class FolderScanner
{
    public static void Scan<T>(
        T root,
        Func<T, IEnumerable<T>> getChildren,
        Action<T> scanFiles,
        Action<T, Exception> onError,
        Action checkCancellation)
    {
        var pending = new Stack<T>();
        pending.Push(root);

        while (pending.TryPop(out var current))
        {
            checkCancellation();

            // Schedule children before reading files so a file error cannot hide descendants.
            try
            {
                foreach (var child in getChildren(current))
                {
                    checkCancellation();
                    pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Already yielded children remain scheduled if enumeration fails partway through.
                onError(current, ex);
            }

            checkCancellation();
            try
            {
                scanFiles(current);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onError(current, ex);
            }
        }
    }
}
