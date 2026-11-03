using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClipBridge.Windows;
internal sealed class DevicesForm : Form
{
    private readonly DeviceHub _hub;
    private readonly FlowLayoutPanel _rows=new() { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16) };
    private readonly Label _status=new() { Dock=DockStyle.Bottom,Height=54,Padding=new Padding(16,8,16,8),Text="同一 Wi-Fi 或热点内自动搜索；配对请求不会自动打开本窗口。" };
    private readonly System.Windows.Forms.Timer _timer=new() { Interval=1000 };
    private string _signature="";
    public DevicesForm(DeviceHub hub)
    {
        _hub=hub; Text="ClipBridge · 设备与配对"; Icon=AppIcon.Load(); Size=new Size(620,700); MinimumSize=new Size(500,500); StartPosition=FormStartPosition.CenterParent; Font=Ui.Font(10f); BackColor=Ui.Canvas; ForeColor=Ui.Ink;
        Controls.Add(_rows); Controls.Add(_status);
        _timer.Tick+=(_,_)=> { if(Visible) RefreshRows(); }; _timer.Start();
        Shown+=(_,_)=> { _hub.RefreshDiscovery(); RefreshRows(); }; FormClosed+=(_,_)=>_timer.Dispose();
    }
    private void Heading(string title) => _rows.Controls.Add(new Label { Text=title,AutoSize=true,Font=Ui.Font(13f,FontStyle.Bold),Margin=new Padding(4,16,4,8) });
    private Button Action(string label,Action callback)
    {
        var button=new Button { Text=label,AutoSize=true,MinimumSize=new Size(96,36),FlatStyle=FlatStyle.Flat,Margin=new Padding(4),BackColor=Ui.Surface,ForeColor=Ui.Ink };
        button.Click+=(_,_)=> { try { callback(); } catch(Exception e) { _status.Text=e.Message; } }; return button;
    }
    private FlowLayoutPanel Card(string title,string detail)
    {
        var box=new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Width=Math.Max(420,_rows.ClientSize.Width-44),Padding=new Padding(14),Margin=new Padding(2,4,2,8),BackColor=Ui.Surface };
        box.Controls.Add(new Label { Text=title,AutoSize=true,Font=Ui.Font(11f,FontStyle.Bold),MaximumSize=new Size(470,0) });
        box.Controls.Add(new Label { Text=detail,AutoSize=true,MaximumSize=new Size(470,0),Margin=new Padding(3,8,3,8) }); _rows.Controls.Add(box); return box;
    }
    private void RefreshRows()
    {
        var nearby=_hub.Devices(); var requests=_hub.Pending.Values.OrderBy(p=>p.Session).ToArray(); var trusted=_hub.Trusted();
        string signature=string.Join("|",nearby.Select(p=>$"{p.Id}:{p.Address}:{p.Authenticated}"))+string.Join("|",requests.Select(p=>$"{p.Session}:{p.Id}:{p.Name}:{p.Address}:{p.Code}:{p.Expires.Ticks}:{p.Decision.Task.IsCompleted}"))+string.Join("|",trusted.Select(t=>$"{t.Id}:{t.Active}"));
        if(signature==_signature && _rows.Controls.Count>0) return; _signature=signature;
        _rows.SuspendLayout(); foreach(Control row in _rows.Controls.Cast<Control>().ToArray()) row.Dispose();
        if(requests.Length>0) Heading("核对两端验证码");
        foreach(var p in requests)
        {
            var card=Card(p.Name+" · "+p.Address,"请核对手机与电脑上的数字完全一致。\n有效期至 "+p.Expires.ToLocalTime().ToString("HH:mm:ss"));
            card.Controls.Add(new Label { Text=p.Code,AutoSize=true,Font=Ui.Font(28f,FontStyle.Bold),ForeColor=Color.RoyalBlue });
            var actions=new FlowLayoutPanel { AutoSize=true,WrapContents=false };
            if(!p.Decision.Task.IsCompleted) { actions.Controls.Add(Action("确认一致",()=>_hub.Decide(p,true))); actions.Controls.Add(Action("拒绝 / 不一致",()=>_hub.Decide(p,false))); }
            else actions.Controls.Add(new Label { Text="已确认，等待另一台设备…",AutoSize=true });
            card.Controls.Add(actions);
        }
        var search=Card("搜索附近设备","切换网络后可立即重新搜索；不会中断正在进行的传输。");
        search.Controls.Add(Action("立即搜索",()=> { _hub.RefreshDiscovery(); _status.Text="正在重新搜索附近设备"; }));
        var scan=Card("扫码配对","在手机互传点击“扫码配对”，扫描这台电脑的二维码。");
        scan.Controls.Add(Action("显示二维码",()=> { using var qr=new PairingQrForm(_hub); qr.ShowDialog(this); }));
        Heading("我的设备");
        foreach(var t in trusted)
        {
            var online=nearby.FirstOrDefault(p=>p.Id==t.Id && p.Authenticated);
            var card=Card(t.Name,(online!=null?"已连接 · "+online.Address:t.Active?"离线":"配对确认恢复中")+" · "+t.Platform);
            card.Controls.Add(Action("取消配对",()=> { _hub.Revoke(t.Id); _status.Text="已取消该设备配对"; }));
        }
        if(trusted.Length==0) Card("还没有配对设备","在下面选择设备发起配对，或在手机上选择这台电脑。");
        Heading("附近设备");
        foreach(var p in nearby.Where(p=>trusted.All(t=>t.Id!=p.Id)))
        {
            var card=Card(p.Name,p.Platform+" · "+p.Address+" · 未配对");
            card.Controls.Add(Action("开始配对",()=> { _status.Text="正在建立配对，请在两端核对验证码"; _=_hub.PairAsync(p); }));
        }
        if(!nearby.Any(p=>trusted.All(t=>t.Id!=p.Id))) Card("正在搜索附近设备…","请让两台设备连接同一 Wi-Fi 或手机热点，并打开互传。网络隔离或防火墙可能阻止发现。");
        var manual=Card("按 IP 发起配对","自动搜索不可用时，填写另一台设备的局域网 IPv4，仍需要在两端确认验证码。");
        var input=new TextBox { Width=330,PlaceholderText="例如 192.168.1.8",AccessibleName="设备局域网 IPv4 地址" }; manual.Controls.Add(input);
        manual.Controls.Add(Action("连接此地址",()=> { if(!System.Net.IPAddress.TryParse(input.Text.Trim(),out var address) || !SharedProtocol.IsLocalAddress(address)) throw new ArgumentException("请输入局域网 IPv4 地址"); _=_hub.PairAsync(new("",input.Text.Trim(),"android",address.ToString(),0,false)); _status.Text="正在发起配对，请核对两端验证码"; }));
        _rows.ResumeLayout();
    }
}
