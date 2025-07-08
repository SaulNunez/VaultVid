using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace VideoHostingService.Services;

public class RedisCacheService(IDistributedCache cache)
{
    public T? GetCacheData<T>(string key)
    {
        var jsonData = cache.GetString(key);
        if (jsonData == null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(jsonData);
    }

    public void SetCacheData<T>(string key, T data, TimeSpan cacheDuration)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = cacheDuration
        };

        var jsonData = JsonSerializer.Serialize(data);
        cache.SetString(key, jsonData, options);
    }
}