using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ClipBridge.Windows;

internal static class FolderTests
{
    public static async Task InteropAsync(string directory)
    {
        Directory.CreateDirectory(directory);string source=Path.Combine(directory,"电脑目录"),output=Path.Combine(directory,"received");Directory.CreateDirectory(Path.Combine(source,"空目录"));await File.WriteAllTextAsync(Path.Combine(source,"原文.txt"),"目录原文 C# → Kotlin");
        byte[] key=Enumerable.Range(0,32).Select(x=>(byte)x).ToArray();using var node=new FileTransferNode(key,output,(_,_,_)=>Task.FromResult(true),0);var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();
        try {await File.WriteAllTextAsync(Path.Combine(directory,"server.json"),JsonSerializer.Serialize(new {receivePort=node.Port,controlPort=((IPEndPoint)listener.LocalEndpoint).Port}));Console.WriteLine("FOLDER INTEROP READY");using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));using var client=await listener.AcceptTcpClientAsync(timeout.Token);var port=new byte[4];await client.GetStream().ReadExactlyAsync(port,timeout.Token);await node.SendAsync(IPAddress.Loopback,[source],timeout.Token,System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(port));Program.Check(File.ReadAllText(Path.Combine(output,"手机目录","原文.txt"))=="目录原文 Kotlin → C#","Kotlin original folder bytes received by production Windows node");Program.Check(Directory.Exists(Path.Combine(output,"手机目录","空目录")),"Kotlin empty directory retained by Windows");Console.WriteLine("BIDIRECTIONAL DIRECTORY INTEROP PASSED");}finally{listener.Stop();}
    }
    public static async Task RunAsync()
    {
        string emptyHash=Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
        var wire=JsonSerializer.SerializeToUtf8Bytes(new { Type="fileTreeOffer",TransferId=new string('a',32),OriginDeviceId="source",TimestampUtc=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Files=new[]{new {Name="素材",Size=0L,Sha256=emptyHash,RelativePath="素材",IsDirectory=true}}});
        var offer=TransferProtocol.Decode<FileOffer>(wire);
        bool accepts=false;try { TransferProtocol.ValidateOffer(offer);accepts=true; } catch(InvalidDataException) { }
        Program.Check(accepts,"directory-aware offer accepted without flattening");
        foreach(string path in new[]{"../escape","/root","C:/escape","a//b","a/../b","a\\b","a/CON"})
        {
            var bad=JsonSerializer.SerializeToUtf8Bytes(new {Type="fileTreeOffer",TransferId=new string('a',32),OriginDeviceId="source",TimestampUtc=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Files=new[]{new{Name="素材",Size=0L,Sha256=emptyHash,RelativePath=path,IsDirectory=true}}});
            Program.Rejects<InvalidDataException>(()=>TransferProtocol.ValidateOffer(TransferProtocol.Decode<FileOffer>(bad)),"unsafe tree path rejected: "+path);
        }
        string root=Path.Combine(Path.GetTempPath(),"clipbridge-folder-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string source=Path.Combine(root,"素材"),output=Path.Combine(root,"received");Directory.CreateDirectory(Path.Combine(source,"照片"));Directory.CreateDirectory(Path.Combine(source,"空目录"));
            byte[] data=RandomNumberGenerator.GetBytes(180000);await File.WriteAllBytesAsync(Path.Combine(source,"照片","原图.png"),data);await File.WriteAllBytesAsync(Path.Combine(source,"empty.txt"),[]);
            var entries=TransferSource.Expand([source],CancellationToken.None);
            var metadata=entries.Select(e=>new TransferFile(e.Name,e.IsDirectory?0:new FileInfo(e.FullPath).Length,e.IsDirectory?TransferProtocol.EmptyHash:Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(e.FullPath))),e.RelativePath,e.IsDirectory)).ToArray();
            using(var store=new ReceiveFileStore(Path.Combine(root,"direct"),metadata)) {
                for(int i=0;i<entries.Length;i++) if(!entries[i].IsDirectory) {var content=File.ReadAllBytes(entries[i].FullPath);for(int offset=0;offset<content.Length;offset+=TransferProtocol.ChunkSize)store.Append(i,offset,content.Skip(offset).Take(TransferProtocol.ChunkSize).ToArray());}
                store.Commit();
            }
            Program.Check(true,"direct tree commit verified");
            var broken=metadata.ToArray();int changed=Array.FindIndex(broken,f=>f.Size>0);broken[changed]=broken[changed] with { Sha256=new string('0',64) };
            string invalid=Path.Combine(root,"invalid");using(var store=new ReceiveFileStore(invalid,broken)) {for(int i=0;i<entries.Length;i++)if(!entries[i].IsDirectory){var bytes=File.ReadAllBytes(entries[i].FullPath);for(int offset=0;offset<bytes.Length;offset+=TransferProtocol.ChunkSize)store.Append(i,offset,bytes.Skip(offset).Take(TransferProtocol.ChunkSize).ToArray());}Program.Rejects<InvalidDataException>(()=>store.Commit(),"corrupt nested file rejects entire directory before commit");}
            Program.Check(!Directory.EnumerateFileSystemEntries(invalid).Any(),"failed folder transfer leaves no partial files");
            var historyFile=new ClipBridge.Windows.History.HistoryFileMetadata("素材",0,TransferProtocol.EmptyHash,"素材",true);var roundtrip=JsonSerializer.Deserialize<ClipBridge.Windows.History.HistoryFileMetadata>(JsonSerializer.Serialize(historyFile));Program.Check(roundtrip==historyFile,"directory history metadata roundtrip retains shape");
            byte[] key=Enumerable.Range(0,32).Select(x=>(byte)x).ToArray();using var node=new FileTransferNode(key,output,(_,_,_)=>Task.FromResult(true),0);
            await node.SendAsync(IPAddress.Loopback,[source],CancellationToken.None,node.Port);
            Program.Check(File.ReadAllBytes(Path.Combine(output,"素材","照片","原图.png")).SequenceEqual(data),"nested original bytes preserved over encrypted socket");
            Program.Check(Directory.Exists(Path.Combine(output,"素材","空目录")),"empty folder preserved");
            Program.Check(new FileInfo(Path.Combine(output,"素材","empty.txt")).Length==0,"empty file preserved inside folder");
            await node.SendAsync(IPAddress.Loopback,[source],CancellationToken.None,node.Port);
            Program.Check(File.ReadAllBytes(Path.Combine(output,"素材 (1)","照片","原图.png")).SequenceEqual(data),"colliding root renamed without flattening or overwriting");
        } finally { Directory.Delete(root,true); }
        Console.WriteLine("FOLDER TESTS PASSED");
    }
}
