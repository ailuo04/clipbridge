using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
namespace ClipBridge.Windows;
// A wire Session ID is attacker-controlled. Ownership and UI decisions are tied to object identity.
internal sealed class PairingSessions<T> where T : class
{
    private readonly ConcurrentDictionary<string,object> _owners=new();
    public readonly ConcurrentDictionary<string,T> Pending=new();
    public bool Reserve(string session,object owner) => PairingProtocol.ValidId(session) && _owners.TryAdd(session,owner);
    public void Publish(string session,object owner,T request)
    {
        if(!_owners.TryGetValue(session,out var current) || !ReferenceEquals(current,owner) || !Pending.TryAdd(session,request)) throw new IOException("重复的配对会话");
    }
    public bool IsCurrent(string session,T request) => Pending.TryGetValue(session,out var current) && ReferenceEquals(current,request);
    public void Release(string session,object owner,T? request)
    {
        if(!_owners.TryGetValue(session,out var current) || !ReferenceEquals(current,owner)) return;
        if(request!=null) ((ICollection<KeyValuePair<string,T>>)Pending).Remove(new(session,request));
        ((ICollection<KeyValuePair<string,object>>)_owners).Remove(new(session,owner));
    }
}
