using System.Security.Cryptography;
using System.Text;

namespace KeePassXCCardPrint.Services;

internal sealed record CloudActivity(string Stage, string Detail, DateTime ChangedUtc);
internal enum CloudSaveDecision { Unchanged, Upload, Download, Conflict }
internal sealed class CloudSaveConflictException : InvalidOperationException
{
    internal CloudSaveConflictException() : base("Both local and cloud configuration changed. Choose a copy in Settings.") { }
}

internal static class CloudSavePolicy
{
    internal static void Migrate(CloudSyncJournal journal)
    {
        // A pending legacy journal cannot prove its previous remote baseline.
        // Never guess it; an unknown divergent copy requires a user decision.
        if (journal.ConfirmedSnapshotId == Guid.Empty && !journal.Pending &&
            journal.SnapshotId != Guid.Empty && journal.LastSuccessfulUtc != default)
        {
            journal.ConfirmedSnapshotId = journal.SnapshotId;
            journal.ConfirmedSignature = journal.LocalSignature;
        }
    }

    internal static bool NeedsUpload(CloudSyncJournal journal, string current) =>
        journal.Pending || journal.SnapshotId == Guid.Empty || journal.LocalSignature != current;

    internal static string ManifestSignature(CloudArchiveManifest manifest)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in manifest.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
            sha.AppendData(Encoding.UTF8.GetBytes(file.Path + ":" + file.Sha256.ToLowerInvariant() + "\n"));
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    internal static CloudSaveDecision Decide(CloudSyncJournal journal, string current,
        CloudArchiveManifest? remote, bool forceUpload = false, bool forceDownload = false)
    {
        if (forceUpload) return CloudSaveDecision.Upload;
        if (forceDownload) return CloudSaveDecision.Download;
        if (remote is null) return CloudSaveDecision.Upload;
        var remoteSignature = ManifestSignature(remote);
        if (remoteSignature == current) return CloudSaveDecision.Unchanged;
        if (!NeedsUpload(journal, current)) return CloudSaveDecision.Download;
        // A retry may find the exact candidate that was sent before a crash or
        // verification error, even if the user has since made another change.
        var previousCandidate = remote.SnapshotId == journal.SnapshotId && remoteSignature == journal.LocalSignature;
        var confirmedBaseline = remote.SnapshotId == journal.ConfirmedSnapshotId && remoteSignature == journal.ConfirmedSignature;
        return previousCandidate || confirmedBaseline ? CloudSaveDecision.Upload : CloudSaveDecision.Conflict;
    }

    internal static void Confirm(CloudSyncJournal journal, CloudArchiveManifest remote,
        string publishedSignature, string current)
    {
        journal.SnapshotId = remote.SnapshotId;
        journal.SavedAtUtc = remote.SavedAtUtc;
        journal.LocalSignature = publishedSignature;
        journal.ConfirmedSnapshotId = remote.SnapshotId;
        journal.ConfirmedSignature = publishedSignature;
        journal.Pending = current != publishedSignature;
    }
}
