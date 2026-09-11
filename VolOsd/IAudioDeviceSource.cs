using System;
using System.Collections.Generic;

namespace VolOsd
{
    /// <summary>
    /// The slice of AudioMonitor that KnobRelay actually depends on. Exists so KnobRelay's logic -
    /// card-group matching, delta computation, the rate limiter - can be unit tested against a fake
    /// device graph instead of real Core Audio hardware. AudioMonitor implements this directly; nothing
    /// about its real behavior changes.
    /// </summary>
    public interface IAudioDeviceSource
    {
        string DefaultDeviceId { get; }
        List<RenderDevice> GetRenderDevices();
        bool TryGetVolume(string deviceId, out float volume);
        bool TrySetVolume(string deviceId, float volume);
        event Action<VolumeChange>? VolumeChanged;
    }
}
