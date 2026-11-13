using System;

namespace ClipBridge.Windows;

internal sealed record PeerSnapshot(
    string Name,
    string Address,
    int Port,
    bool Manual,
    bool RecentlySeen,
    DateTimeOffset? LastSeen,
    string PeerId = "")
{
    // "android" / "windows" from the pairing record; empty for legacy peers.
    public string Platform { get; init; } = "";

    // Phones and computers get different avatars. Unknown platforms fall back
    // to the device name, and to a phone when that says nothing either.
    public bool IsComputer => Platform.Length > 0
        ? !string.Equals(Platform, "android", StringComparison.OrdinalIgnoreCase) && !string.Equals(Platform, "ios", StringComparison.OrdinalIgnoreCase)
        : Name.StartsWith("DESKTOP", StringComparison.OrdinalIgnoreCase) || Name.StartsWith("LAPTOP", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("PC", StringComparison.OrdinalIgnoreCase) || Name.Contains("电脑", StringComparison.Ordinal);
}
