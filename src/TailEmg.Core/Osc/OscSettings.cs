namespace TailEmg.Core.Osc;

/// <summary>Defaults live here (not in the UI). Mutable so it can be edited at runtime.</summary>
public sealed class OscSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9000;
    public string Address { get; set; } = "/avatar/parameters/TailEMG";
    public double RateHz { get; set; } = 30;
    public bool Enabled { get; set; } = true;
}
