using System;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace ClipBridge.Windows;
internal sealed class WindowsTrustStore : IDeviceStore
{
    private sealed record State(string Id,string Name,DeviceTrust[] Devices);
    private readonly object _gate=new();
    private readonly string _path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClipBridge","devices.bin");
    private State _state;
    public string DeviceId => _state.Id;
    public string Name => _state.Name;
    public WindowsTrustStore()
    {
        _state=File.Exists(_path)?JsonSerializer.Deserialize<State>(ProtectedKeyStore.Unprotect(File.ReadAllBytes(_path)))!:new(Guid.NewGuid().ToString("N"),Environment.MachineName,Array.Empty<DeviceTrust>());
        if(_state==null || !PairingProtocol.ValidId(_state.Id) || _state.Devices.Length>32 || _state.Devices.Any(t=>!PairingProtocol.ValidId(t.Id) || t.Key.Length!=32)) throw new InvalidDataException("设备配对记录损坏，请恢复备份");
        Persist(_state);
    }
    public DeviceTrust[] Records() { lock(_gate) return _state.Devices.ToArray(); }
    public void Save(DeviceTrust record)
    {
        lock(_gate)
        {
            if(!PairingProtocol.ValidId(record.Id) || record.Key.Length!=32 || (!_state.Devices.Any(t=>t.Id==record.Id) && _state.Devices.Length>=32)) throw new InvalidDataException("信任设备数量已达上限");
            var next=_state with { Devices=_state.Devices.Where(t=>t.Id!=record.Id).Append(record).ToArray() };
            // Authenticated presence refreshes RAM without rewriting DPAPI storage every three seconds.
            var old=_state.Devices.FirstOrDefault(t=>t.Id==record.Id);
            if(old==null || old.Address!=record.Address || old.Name!=record.Name || old.Active!=record.Active || !old.Key.AsSpan().SequenceEqual(record.Key)) Persist(next);
            _state=next;
        }
    }
    public void Remove(string id) { lock(_gate) { var next=_state with { Devices=_state.Devices.Where(t=>t.Id!=id).ToArray() }; Persist(next); _state=next; } }
    private void Persist(State value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); string temp=_path+".tmp";
        File.WriteAllBytes(temp,ProtectedKeyStore.Protect(JsonSerializer.SerializeToUtf8Bytes(value))); File.Move(temp,_path,true);
    }
}
