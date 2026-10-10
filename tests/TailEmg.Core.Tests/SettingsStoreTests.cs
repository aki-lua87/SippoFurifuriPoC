using TailEmg.Core.Osc;
using TailEmg.Core.Processing;
using TailEmg.Core.Settings;

namespace TailEmg.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tailemg-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public void Defaults_MatchRuntimeSettings()
    {
        var d = AppSettings.Default;
        var osc = new OscSettings();
        var proc = new ProcessingSettings();
        Assert.Equal(osc.Host, d.OscHost);
        Assert.Equal(osc.Port, d.OscPort);
        Assert.Equal(osc.Address, d.OscAddress);
        Assert.Equal(proc.Deadzone, d.Deadzone);
        Assert.Equal(proc.SmoothingAlpha, d.Smoothing);
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var s = AppSettings.Default with
        {
            OscHost = "192.168.0.5",
            OscPort = 9001,
            Deadzone = 0.1,
            Gamma = 2.0,
            Curve = ResponseCurveType.Gamma,
            Rest = 12,
            Max = 3900,
            InputSource = "MyoWareBle",
            LastBleAddress = 0xE08CFEA56CA2,
            LastBleName = "MyoWareSensor1",
        };

        Assert.True(SettingsStore.Save(FilePath, s));
        Assert.Equal(s, SettingsStore.Load(FilePath));
    }

    [Fact]
    public void Missing_ReturnsDefaults()
        => Assert.Equal(AppSettings.Default, SettingsStore.Load(FilePath));

    [Fact]
    public void Corrupt_FallsBackToDefaults_AndKeepsBackup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json");

        Assert.Equal(AppSettings.Default, SettingsStore.Load(FilePath));
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void OutOfRangeValues_AreReplacedByDefaults_ValidOnesKept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            { "OscPort": 99999, "OscAddress": "no-slash", "Smoothing": 5, "Deadzone": 0.2, "Max": 3000 }
            """);

        var s = SettingsStore.Load(FilePath);

        Assert.Equal(AppSettings.Default.OscPort, s.OscPort);
        Assert.Equal(AppSettings.Default.OscAddress, s.OscAddress);
        Assert.Equal(AppSettings.Default.Smoothing, s.Smoothing);
        Assert.Equal(0.2, s.Deadzone);
        Assert.Equal(3000, s.Max);
    }
}
