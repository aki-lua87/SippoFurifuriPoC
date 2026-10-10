using System.Text.Json;
using System.Text.Json.Serialization;
using TailEmg.Core.Osc;
using TailEmg.Core.Processing;

namespace TailEmg.Core.Settings;

/// <summary>Everything persisted between runs. Defaults come from the runtime settings classes (single source).</summary>
public sealed record AppSettings
{
    private static readonly OscSettings DefaultOsc = new();
    private static readonly ProcessingSettings DefaultProcessing = new();

    public string OscHost { get; init; } = DefaultOsc.Host;
    public int OscPort { get; init; } = DefaultOsc.Port;
    public string OscAddress { get; init; } = DefaultOsc.Address;
    public double OscRateHz { get; init; } = DefaultOsc.RateHz;

    public double Deadzone { get; init; } = DefaultProcessing.Deadzone;
    public double Gamma { get; init; } = DefaultProcessing.Gamma;
    public double Smoothing { get; init; } = DefaultProcessing.SmoothingAlpha;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ResponseCurveType Curve { get; init; } = DefaultProcessing.Curve;

    public double Rest { get; init; } = DefaultProcessing.Rest;
    public double Max { get; init; } = DefaultProcessing.Max;

    /// <summary>"Fake" or "MyoWareBle".</summary>
    public string InputSource { get; init; } = "Fake";
    public ulong? LastBleAddress { get; init; }
    public string? LastBleName { get; init; }

    public static AppSettings Default { get; } = new();

    /// <summary>Replaces any out-of-range / unusable field with its default so a hand-edited file cannot break the app.</summary>
    public AppSettings Sanitize()
    {
        var d = Default;
        return this with
        {
            OscHost = string.IsNullOrWhiteSpace(OscHost) ? d.OscHost : OscHost.Trim(),
            OscPort = OscPort is >= 1 and <= 65535 ? OscPort : d.OscPort,
            OscAddress = !string.IsNullOrWhiteSpace(OscAddress) && OscAddress.Trim().StartsWith('/') ? OscAddress.Trim() : d.OscAddress,
            OscRateHz = InRange(OscRateHz, 1, 240) ? OscRateHz : d.OscRateHz,
            Deadzone = InRange(Deadzone, 0, 0.99) ? Deadzone : d.Deadzone,
            Gamma = double.IsFinite(Gamma) && Gamma >= 0.01 ? Gamma : d.Gamma,
            Smoothing = InRange(Smoothing, 0, 1) ? Smoothing : d.Smoothing,
            Curve = Enum.IsDefined(Curve) ? Curve : d.Curve,
            Rest = double.IsFinite(Rest) ? Rest : d.Rest,
            Max = double.IsFinite(Max) ? Max : d.Max,
            InputSource = string.IsNullOrWhiteSpace(InputSource) ? d.InputSource : InputSource,
        };
    }

    private static bool InRange(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Missing or corrupt file → defaults (a corrupt file is kept as *.bad). Never throws.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return AppSettings.Default;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                if (loaded is not null)
                {
                    return loaded.Sanitize();
                }
            }
            catch (JsonException)
            {
            }

            TryKeepCorrupt(path);
        }
        catch (Exception)
        {
            // Unreadable (locked, permissions): run with defaults.
        }

        return AppSettings.Default;
    }

    /// <summary>Atomic write (temp file + move). Returns false instead of throwing on IO problems.</summary>
    public static bool Save(string path, AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryKeepCorrupt(string path)
    {
        try
        {
            File.Move(path, path + ".bad", overwrite: true);
        }
        catch (Exception)
        {
        }
    }
}
