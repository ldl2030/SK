using System;

namespace TestPlatform
{
    /// <summary>
    /// Common log-event contract for devices that publish communication logs.
    /// </summary>
    public interface IDeviceLogSource
    {
        event Action<string> LogInfo;
        event Action<string> LogError;
    }
}
