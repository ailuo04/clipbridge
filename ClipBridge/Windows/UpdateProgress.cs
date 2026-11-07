using System;

namespace ClipBridge.Windows;

internal enum UpdateStage { Downloading, Verifying, Extracting, Waiting, Installing, Restarting, Completed }

internal sealed record UpdateProgress(UpdateStage Stage, long BytesReceived = 0, long TotalBytes = 0, double BytesPerSecond = 0)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesReceived / TotalBytes, 0, 1) : 0;
    public double OverallPercent => Stage switch
    {
        UpdateStage.Downloading => Fraction * 80,
        UpdateStage.Verifying => 80 + Fraction * 2,
        UpdateStage.Extracting => 82 + Fraction * 8,
        UpdateStage.Waiting => 90,
        UpdateStage.Installing => 92 + Fraction * 6,
        UpdateStage.Restarting => 99,
        UpdateStage.Completed => 100,
        _ => 0
    };
}
