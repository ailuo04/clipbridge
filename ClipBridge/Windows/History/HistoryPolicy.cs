using System.IO;
using System.Linq;
using System.Text;
using ClipBridge.Windows;

namespace ClipBridge.Windows.History;

internal static class HistoryPolicy
{
    public const int SchemaVersion = 1;
    public const long SevenDaysMilliseconds = 7L * 24 * 60 * 60 * 1000;

    public static bool IsVisible(long createdUtc, long nowUtc) =>
        createdUtc > nowUtc - SevenDaysMilliseconds && createdUtc <= nowUtc;

    public static void ValidateEvent(HistorySyncEvent value, string authenticatedPeerId, long nowUtc)
    {
        if (value is null) throw new InvalidDataException("History event is required.");
        RequireId(value.EventId, "eventId");
        RequireId(value.PeerId, "peerId");
        RequireId(value.SenderDeviceId, "senderDeviceId");
        RequireId(authenticatedPeerId, "authenticated peerId");
        if (value.SchemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported history event schema.");
        if (value.PeerId != authenticatedPeerId) throw new InvalidDataException("History event peer does not match the authenticated route.");
        if (value.CreatedUtc > nowUtc) throw new InvalidDataException("History event timestamp is in the future.");
        if (value.Files is null) throw new InvalidDataException("History files are required.");
        if (value.Text is not null && Encoding.UTF8.GetByteCount(value.Text) > SharedProtocol.MaxTextBytes)
            throw new InvalidDataException("History text exceeds the shared protocol limit.");

        foreach (HistoryFileMetadata file in value.Files)
        {
            if (file is null) throw new InvalidDataException("History file metadata is required.");
            TransferProtocol.ValidateFileName(file.Name);
            if (file.Size < 0 || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid history file metadata.");
        }
    }

    public static void ValidateTombstone(HistoryTombstone value, string authenticatedPeerId)
    {
        if (value is null) throw new InvalidDataException("History tombstone is required.");
        RequireId(value.EventId, "eventId");
        RequireId(value.PeerId, "peerId");
        RequireId(authenticatedPeerId, "authenticated peerId");
        if (value.PeerId != authenticatedPeerId) throw new InvalidDataException("History tombstone peer does not match the authenticated route.");
    }

    private static void RequireId(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"History {name} is required.");
    }
}