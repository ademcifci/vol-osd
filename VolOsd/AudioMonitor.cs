using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VolOsd
{
    public readonly record struct VolumeChange(string DeviceId, string DeviceName, float Volume, bool Muted, bool IsDefault);

    public readonly record struct RenderDevice(string Id, string Name);

    /// <summary>
    /// Watches active playback devices' volume via Core Audio and raises an event per real change.
    /// Read-only: this app never writes endpoint volume.
    /// </summary>
    public sealed class AudioMonitor : IMMNotificationClient, IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new();
        private readonly Dictionary<string, WatchedDevice> _watched = new();
        private readonly object _devicesLock = new();
        private volatile bool _disposed;
        private int _refreshPending;
        private volatile string _defaultDeviceId = "";

        public event Action<VolumeChange>? VolumeChanged;

        public string DefaultDeviceId => _defaultDeviceId;

        private sealed class WatchedDevice
        {
            public required MMDevice Device;
            public required string Name;
            public float LastVolume = -1f;
            public bool LastMuted;
        }

        public AudioMonitor()
        {
            try
            {
                _enumerator.RegisterEndpointNotificationCallback(this);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"RegisterEndpointNotificationCallback failed: {ex.Message}");
            }

            RefreshDefaultDeviceId();
            RefreshDeviceList();
        }

        private void RefreshDefaultDeviceId()
        {
            try
            {
                using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _defaultDeviceId = device.ID;
            }
            catch (Exception ex)
            {
                _defaultDeviceId = "";
                Diagnostics.Log($"No default render device: {ex.Message}");
            }
        }

        public List<RenderDevice> GetRenderDevices()
        {
            var result = new List<RenderDevice>();
            lock (_devicesLock)
            {
                foreach (var kv in _watched)
                    result.Add(new RenderDevice(kv.Key, kv.Value.Name));
            }
            return result;
        }

        public bool TryGetVolume(string deviceId, out float volume)
        {
            volume = 0f;
            if (string.IsNullOrEmpty(deviceId)) return false;

            lock (_devicesLock)
            {
                if (!_watched.TryGetValue(deviceId, out var watched)) return false;
                try
                {
                    volume = watched.Device.AudioEndpointVolume.MasterVolumeLevelScalar;
                    return true;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log($"TryGetVolume failed: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Rebuilds the watch list. Never throws: at login the audio service may not be ready yet,
        /// and devices can disappear mid-enumeration, so a failure here must not take the app down.
        /// </summary>
        private void RefreshDeviceList()
        {
            lock (_devicesLock)
            {
                if (_disposed) return;

                MMDeviceCollection devices;
                try
                {
                    devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log($"Device enumeration failed: {ex.Message}");
                    return;
                }

                var current = new HashSet<string>();

                foreach (var device in devices)
                {
                    try
                    {
                        current.Add(device.ID);

                        if (_watched.ContainsKey(device.ID))
                        {
                            device.Dispose();
                            continue;
                        }

                        var watched = new WatchedDevice { Device = device, Name = SafeName(device) };
                        device.AudioEndpointVolume.OnVolumeNotification += data => OnVolumeNotification(watched, data);
                        _watched[device.ID] = watched;
                        Diagnostics.Log($"Watching render device: {watched.Name}");
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log($"Skipping unusable device: {ex.Message}");
                        try { device.Dispose(); } catch { /* already gone */ }
                    }
                }

                var stale = new List<string>();
                foreach (var kv in _watched)
                {
                    if (!current.Contains(kv.Key))
                        stale.Add(kv.Key);
                }

                foreach (var id in stale)
                {
                    Diagnostics.Log($"No longer active, dropping: {_watched[id].Name}");
                    try { _watched[id].Device.Dispose(); } catch { /* already gone */ }
                    _watched.Remove(id);
                }
            }
        }

        /// <summary>
        /// IMMNotificationClient callbacks must return promptly and must not call back into the
        /// enumerator - doing so risks deadlocking the audio service. So device changes are handled
        /// off the callback thread, with a short delay to let a plug/unplug's burst of callbacks settle.
        /// </summary>
        private void ScheduleRefresh()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _refreshPending, 1) == 1) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(250);
                Interlocked.Exchange(ref _refreshPending, 0);
                if (!_disposed) RefreshDeviceList();
            });
        }

        private static string SafeName(MMDevice device)
        {
            try { return device.FriendlyName; }
            catch { return device.ID; }
        }

        private void OnVolumeNotification(WatchedDevice watched, AudioVolumeNotificationData data)
        {
            if (_disposed) return;

            // Drivers fire bursts of identical notifications per knob detent; drop exact repeats for
            // this device. Changes on *other* devices still come through, since callers need to see
            // which endpoints moved together.
            lock (_devicesLock)
            {
                if (Math.Abs(data.MasterVolume - watched.LastVolume) < 0.0005f && data.Muted == watched.LastMuted)
                    return;

                watched.LastVolume = data.MasterVolume;
                watched.LastMuted = data.Muted;
            }

            string id;
            lock (_devicesLock)
            {
                id = "";
                foreach (var kv in _watched)
                {
                    if (ReferenceEquals(kv.Value, watched)) { id = kv.Key; break; }
                }
            }

            VolumeChanged?.Invoke(new VolumeChange(id, watched.Name, data.MasterVolume, data.Muted, id == _defaultDeviceId));
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => ScheduleRefresh();
        public void OnDeviceAdded(string pwstrDeviceId) => ScheduleRefresh();
        public void OnDeviceRemoved(string deviceId) => ScheduleRefresh();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow != DataFlow.Render || role != Role.Multimedia) return;

            _defaultDeviceId = defaultDeviceId ?? "";
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            _disposed = true;

            try { _enumerator.UnregisterEndpointNotificationCallback(this); }
            catch (Exception ex) { Diagnostics.Log($"Unregister callback failed: {ex.Message}"); }

            lock (_devicesLock)
            {
                foreach (var watched in _watched.Values)
                {
                    try { watched.Device.Dispose(); } catch { /* already gone */ }
                }
                _watched.Clear();
            }

            try { _enumerator.Dispose(); } catch { /* already gone */ }
        }
    }
}
