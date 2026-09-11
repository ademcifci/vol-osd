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

        /// <summary>
        /// The real device id FxSound most recently recorded as what it's actually rendering to,
        /// read live from its own registry state - or null if unknown/not found. FxSound always
        /// presents one fixed virtual device to Windows regardless of which physical device it's
        /// secretly routed to (including auto-following whichever was last active, invisibly), so
        /// this is the only live signal available for "what is it actually using right now". See
        /// the remarks on KnobRelay for why that matters and where this was discovered.
        /// </summary>
        string? FxSoundRealPlaybackDeviceId { get; }
    }
}
