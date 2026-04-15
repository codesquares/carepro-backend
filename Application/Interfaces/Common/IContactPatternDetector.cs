using Application.DTOs;

namespace Application.Interfaces.Common
{
    public interface IContactPatternDetector
    {
        ContactDetectionResult Detect(string message);
    }
}
