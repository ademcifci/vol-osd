using System;
using System.Collections.Generic;
using System.Linq;

namespace VolOsd
{
    /// <summary>
    /// Identifies volume events from the X3 hardware knob. The driver keeps sibling endpoints on the
    /// same card in sync (Speakers and SPDIF Out), so a turn may notify on either — match the whole card.
    /// </summary>
    public static class KnobDeviceHelper
    {
        public static bool IsKnobSource(string deviceId, string knobDeviceId, IReadOnlyList<RenderDevice> devices)
        {
            if (string.IsNullOrEmpty(knobDeviceId) || string.IsNullOrEmpty(deviceId))
                return false;

            if (deviceId == knobDeviceId)
                return true;

            var knobCard = CardName(FindName(knobDeviceId, devices));
            if (string.IsNullOrEmpty(knobCard))
                return false;

            return CardName(FindName(deviceId, devices)) == knobCard;
        }

        /// <summary>First-run helper: pick SPDIF Out on the X3 if present, otherwise any X3 endpoint.</summary>
        public static string? TryAutoDetectKnobDevice(IReadOnlyList<RenderDevice> devices)
        {
            var x3 = devices
                .Where(d => d.Name.Contains("Sound Blaster X3", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (x3.Count == 0)
                return null;

            var spdif = x3.FirstOrDefault(d => d.Name.Contains("SPDIF", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(spdif.Id))
                return spdif.Id;

            return x3[0].Id;
        }

        private static string FindName(string deviceId, IReadOnlyList<RenderDevice> devices) =>
            devices.FirstOrDefault(d => d.Id == deviceId).Name ?? "";

        private static string CardName(string friendlyName)
        {
            int open = friendlyName.LastIndexOf('(');
            int close = friendlyName.LastIndexOf(')');
            return open >= 0 && close > open ? friendlyName[(open + 1)..close] : "";
        }
    }
}
