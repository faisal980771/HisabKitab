namespace HisabKitab.Services;
public static class PageLoadingService
{
    public static async Task RunAsync(Func<Task> loadAsync,Action showBlock,Action hideBlock,Func<Task> showSkeleton,Func<Task> hideSkeleton,int blockMs=120,int minSkeletonMs=350)
    {
        var started=DateTime.UtcNow;
        try
        {
            showBlock(); await Task.Yield(); await Task.Delay(blockMs); hideBlock();
            await showSkeleton(); await loadAsync();
            var remain=minSkeletonMs-(int)(DateTime.UtcNow-started).TotalMilliseconds; if(remain>0) await Task.Delay(remain);
        }
        finally{hideBlock();await hideSkeleton();}
    }
}
