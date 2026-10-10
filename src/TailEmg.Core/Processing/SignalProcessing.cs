namespace TailEmg.Core.Processing;

public enum ResponseCurveType
{
    Linear,
    Gamma,
}

/// <summary>Stateless processing steps. Each takes/returns plain doubles so they are trivial to test.</summary>
public static class SignalProcessing
{
    /// <summary>(input - rest) / (max - rest), clamped to 0..1. Returns 0 when max &lt;= rest (invalid calibration).</summary>
    public static double Normalize(double input, double rest, double max)
    {
        var range = max - rest;
        if (!(range > 0) || double.IsNaN(input))
        {
            return 0;
        }

        return Math.Clamp((input - rest) / range, 0, 1);
    }

    /// <summary>Values at or below the deadzone become 0; the rest is remapped onto 0..1.</summary>
    public static double ApplyDeadzone(double input, double deadzone)
    {
        deadzone = Math.Clamp(deadzone, 0, 0.99);
        if (input <= deadzone)
        {
            return 0;
        }

        return Math.Clamp((input - deadzone) / (1 - deadzone), 0, 1);
    }

    public static double ApplyCurve(double input, ResponseCurveType curve, double gamma)
    {
        input = Math.Clamp(input, 0, 1);
        return curve == ResponseCurveType.Gamma
            ? Math.Pow(input, Math.Max(0.01, gamma))
            : input;
    }

    /// <summary>Length of the step that <c>alpha</c> is defined for in <see cref="EmaTimed"/> (the OSC frame at 30 Hz).</summary>
    public const double EmaReferenceSeconds = 1.0 / 30.0;

    /// <summary>filtered = previous + alpha * (current - previous)</summary>
    public static double Ema(double previous, double current, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        return previous + alpha * (current - previous);
    }

    /// <summary>
    /// EMA whose smoothing does not depend on the input rate: <paramref name="alpha"/> is the per-
    /// <see cref="EmaReferenceSeconds"/> coefficient, rescaled for the real elapsed time
    /// (alpha_eff = 1 - (1 - alpha)^(dt / reference)). 50 Hz fake data and ~700 Hz BLE bursts
    /// therefore feel the same. dt of 0 leaves the value unchanged.
    /// </summary>
    public static double EmaTimed(double previous, double current, double alpha, double dtSeconds)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        dtSeconds = Math.Max(0, dtSeconds);
        var effective = alpha >= 1 ? 1 : 1 - Math.Pow(1 - alpha, dtSeconds / EmaReferenceSeconds);
        return previous + effective * (current - previous);
    }
}
