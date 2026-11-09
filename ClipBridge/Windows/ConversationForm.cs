using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClipBridge.Windows.History;
namespace ClipBridge.Windows;

// Compatibility host for isolated conversation testing; the application uses WorkspaceForm.
internal sealed class ConversationForm : Form
{
    public ConversationView View { get; }
    public event Action? HiddenToTray;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowCloseForExit { get; set; }
    public PeerSnapshot? SelectedPeer => View.SelectedPeer;
    public IReadOnlyList<HistoryLocalRecord> VisibleEntries => View.VisibleEntries;
    public string[] PendingAttachments => View.PendingAttachments;
    public ConversationForm(HistoryStore? history, string localDeviceId, Func<PeerSnapshot[]> getPeers, Func<PeerSnapshot, string, CancellationToken, Task<bool>> sendText, Func<PeerSnapshot, string[], CancellationToken, Task> sendFiles, Action openReceiveDirectory, Action manageDevices)
    {
        Text = "互传"; StartPosition = FormStartPosition.CenterScreen; Size = new Size(860, 760); MinimumSize = new Size(620, 540);
        View = new(history, localDeviceId, getPeers, sendText, sendFiles, openReceiveDirectory, manageDevices) { Dock = DockStyle.Fill }; Controls.Add(View);
    }
    public void RefreshPeers(IReadOnlyList<PeerSnapshot>? peers = null, string? preferredPeerId = null) => View.RefreshPeers(peers, preferredPeerId);
    public void RefreshHistory() => View.RefreshHistory();
    public void SetTransfers(TransferProgress[] values) => View.SetTransfers(values);
    public void ApplyTheme() => View.ApplyTheme();
    public void AddAttachments(string[] paths) => View.AddAttachments(paths);
    public Task SendFilesAsync(string[] paths) => View.SendFilesAsync(paths);
    protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !AllowCloseForExit) { e.Cancel = true; Hide(); HiddenToTray?.Invoke(); return; } base.OnFormClosing(e); }
}
