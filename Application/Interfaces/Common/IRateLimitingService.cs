namespace Application.Interfaces.Common
{
    public interface IRateLimitingService
    {
        bool CheckRateLimit(string clientIp);
    }
}