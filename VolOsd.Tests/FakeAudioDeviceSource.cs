using VolOsd;

namespace VolOsd.Tests
{
    /// <summary>
    /// Stands in for AudioMonitor in tests: a small graph of named devices with volumes, plus the
    /// ability to fire VolumeChanged as if a real notification arrived and to inspect every write
    /// KnobRelay actually made. No COM, no hardware.
    /// </summary>
    public sealed class FakeAudioDeviceSource : IAudioDeviceSource
    {
        private readonly Dictionary<string, float> _volumes = new();
        private readonly Dictionary<string, string> _names = new();

        public string DefaultDeviceId { get; set; } = "";
        public event Action<VolumeChange>? VolumeChanged;

        /// <summary>Every TrySetVolume call this instance has ever received, in order.</summary>
        public List<(string DeviceId, float Volume)> Writes { get; } = new();

        public void AddDevice(string id, string name, float initialVolume)
        {
            _names[id] = name;
            _volumes[id] = initialVolume;
        }

        public List<RenderDevice> GetRenderDevices() =>
            _names.Select(kv => new RenderDevice(kv.Key, kv.Value)).ToList();

        public bool TryGetVolume(string deviceId, out float volume) =>
            _volumes.TryGetValue(deviceId, out volume);

        public bool TrySetVolume(string deviceId, float volume)
        {
            if (!_volumes.ContainsKey(deviceId)) return false;
            volume = Math.Clamp(volume, 0f, 1f);
            _volumes[deviceId] = volume;
            Writes.Add((deviceId, volume));
            return true;
        }

        /// <summary>Simulates a real notification arriving for this device, at its currently stored
        /// volume (set it first with SetVolumeDirect if the "hardware" moved outside a TrySetVolume
        /// call, e.g. a physical knob turn).</summary>
        public void RaiseVolumeChanged(string deviceId, float volume, bool muted = false)
        {
            _volumes[deviceId] = volume;
            VolumeChanged?.Invoke(new VolumeChange(deviceId, _names.GetValueOrDefault(deviceId, deviceId), volume, muted, deviceId == DefaultDeviceId));
        }
    }
}
