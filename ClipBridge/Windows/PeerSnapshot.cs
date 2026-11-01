using System;

namespace ClipBridge.Windows;

internal sealed record PeerSnapshot(string Name, string Address, int Port, bool Manual, bool RecentlySeen, DateTimeOffset? LastSeen);
