using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ClipBridge.Windows;

internal sealed class SyncNode : IDisposable
{
	private sealed record Peer(IPEndPoint EndPoint, DateTimeOffset LastSeen, bool Manual = false, string Name = "");

	private readonly byte[] _key;

	private readonly string _pairingKeyText;

	private readonly string _deviceId = Guid.NewGuid().ToString("N");

	private readonly ConcurrentDictionary<string, Peer> _peers = new ConcurrentDictionary<string, Peer>();

	private readonly ConcurrentDictionary<string, long> _seenMessages = new ConcurrentDictionary<string, long>();

	private readonly CancellationTokenSource _stop = new CancellationTokenSource();

	private readonly SemaphoreSlim _connections = new SemaphoreSlim(4);

	private readonly Channel<ClipboardMessage> _outbound = Channel.CreateBounded<ClipboardMessage>(new BoundedChannelOptions(1)
	{
		FullMode = BoundedChannelFullMode.DropOldest,
		SingleReader = true
	});

	private readonly TcpListener _tcpListener;

	private readonly UdpClient _udp;

	private readonly Action<Action> _dispatchToUi;

	private readonly object _clipboardLock = new object();

	private string? _lastClipboardHash;

	private volatile bool _enabled;

	private bool _disposed;

	public bool Enabled => _enabled;

	public event Action<string>? RemoteTextReceived;

	public event Action<string>? StatusChanged;

	public SyncNode(byte[] pairingKey, Action<Action> dispatchToUi)
	{
		_key = SharedProtocol.DeriveKey(pairingKey);
		_pairingKeyText = Convert.ToHexString(pairingKey);
		_dispatchToUi = dispatchToUi;
		_tcpListener = new TcpListener(IPAddress.Any, 45822);
		_udp = new UdpClient(AddressFamily.InterNetwork);
		try
		{
			_udp.EnableBroadcast = true;
			_udp.Client.Bind(new IPEndPoint(IPAddress.Any, 45821));
			_tcpListener.Start(8);
		}
		catch
		{
			_udp.Dispose();
			_tcpListener.Stop();
			throw;
		}
		ReceiveAnnouncementsAsync(_stop.Token);
		AnnounceLoopAsync(_stop.Token);
		AcceptLoopAsync(_stop.Token);
		SendLoopAsync(_stop.Token);
	}

	public void SetEnabled(bool enabled)
	{
		_enabled = enabled;
		lock (_clipboardLock)
		{
			_lastClipboardHash = null;
		}
		Report(enabled ? "同步已开启，等待新复制内容" : "同步已暂停");
	}

	public void AddManualPeer(IPAddress address)
	{
		if (!SharedProtocol.IsLocalAddress(address))
		{
			throw new ArgumentException("Only local IPv4 addresses are allowed.");
		}
		string key = "manual:" + address;
		if (_peers.Count >= 32 && !_peers.ContainsKey(key))
		{
			throw new InvalidOperationException("Too many peers.");
		}
		_peers[key] = new Peer(new IPEndPoint(address, 45822), DateTimeOffset.MinValue, Manual: true);
		Report("已添加手动设备地址");
	}

	public void RemoveManualPeer(IPAddress address)
	{
		if (_peers.TryRemove("manual:" + address, out Peer _))
		{
			Report("已移除手动设备地址");
		}
	}

	public PeerSnapshot[] GetPeerSnapshots()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		return (from peer in (from peer in _peers.Values.ToArray()
				group peer by peer.EndPoint).Select((IGrouping<IPEndPoint, Peer> @group) =>
			{
				Peer peer = (from peer2 in @group
					where !peer2.Manual
					orderby peer2.LastSeen descending
					select peer2).FirstOrDefault();
				bool flag = @group.Any((Peer peer2) => peer2.Manual);
				bool recentlySeen = (object)peer != null && now - peer.LastSeen <= TimeSpan.FromSeconds(15.0);
				string name = ((!string.IsNullOrWhiteSpace(peer?.Name)) ? peer.Name : (flag ? "手动设备" : "同密钥设备"));
				return new PeerSnapshot(name, @group.Key.Address.ToString(), @group.Key.Port, flag, recentlySeen, peer?.LastSeen);
			})
			orderby peer.RecentlySeen descending
			select peer).ThenBy((PeerSnapshot peer) => peer.Address, StringComparer.Ordinal).ToArray();
	}

	public void MarkRemoteTextApplied(string text)
	{
		lock (_clipboardLock)
		{
			_lastClipboardHash = Hash(text);
		}
	}

	public void OnLocalClipboardTextChanged(string text)
	{
		if (!_enabled)
		{
			return;
		}
		if (text.Length == 0)
		{
			lock (_clipboardLock)
			{
				_lastClipboardHash = null;
				return;
			}
		}
		if (Encoding.UTF8.GetByteCount(text) > 262144)
		{
			Report("文本超过 256 KiB，未同步");
		}
		else
		{
			if (string.Equals(text.Trim(), _pairingKeyText, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			string text2 = Hash(text);
			lock (_clipboardLock)
			{
				if (text2 == _lastClipboardHash)
				{
					return;
				}
				_lastClipboardHash = text2;
			}
			_outbound.Writer.TryWrite(new ClipboardMessage("clipboard", Guid.NewGuid().ToString("N"), _deviceId, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
		}
	}

	private byte[] AnnouncementBytes()
	{
		return SharedProtocol.Encrypt(_key, SharedProtocol.Serialize(new Announcement("announce", _deviceId, Environment.MachineName, 45822, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
	}

	private async Task AnnounceLoopAsync(CancellationToken token)
	{
		_ = 2;
		try
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					byte[] bytes = AnnouncementBytes();
					await _udp.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, 45821), token);
					KeyValuePair<string, Peer>[] array = _peers.ToArray();
					for (int i = 0; i < array.Length; i++)
					{
						KeyValuePair<string, Peer> keyValuePair = array[i];
						if (keyValuePair.Value.Manual)
						{
							await _udp.SendAsync(bytes, new IPEndPoint(keyValuePair.Value.EndPoint.Address, 45821), token);
						}
						else if (DateTimeOffset.UtcNow - keyValuePair.Value.LastSeen > TimeSpan.FromSeconds(15.0))
						{
							_peers.TryRemove(keyValuePair.Key, out Peer _);
						}
					}
				}
				catch (SocketException)
				{
					Report("网络发现暂不可用，正在重试");
				}
				await Task.Delay(3000, token);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task ReceiveAnnouncementsAsync(CancellationToken token)
	{
		_ = 2;
		try
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					UdpReceiveResult udpReceiveResult = await _udp.ReceiveAsync(token);
					int num = udpReceiveResult.Buffer.Length;
					bool flag = ((num < 28 || num > 4096) ? true : false);
					if (flag || !SharedProtocol.IsLocalAddress(udpReceiveResult.RemoteEndPoint.Address))
					{
						continue;
					}
					Announcement announcement = JsonSerializer.Deserialize<Announcement>(SharedProtocol.Decrypt(_key, udpReceiveResult.Buffer));
					if ((object)announcement == null || announcement.Type != "announce" || announcement.DeviceId == _deviceId || string.IsNullOrEmpty(announcement.DeviceId) || announcement.DeviceId.Length > 64 || announcement.Port != 45822 || !SharedProtocol.IsFresh(announcement.TimestampUtc))
					{
						continue;
					}
					bool flag2 = !_peers.ContainsKey(announcement.DeviceId);
					if (!flag2 || _peers.Count < 32)
					{
						_peers[announcement.DeviceId] = new Peer(new IPEndPoint(udpReceiveResult.RemoteEndPoint.Address, announcement.Port), DateTimeOffset.UtcNow, Manual: false, (announcement.Name ?? "同密钥设备").Substring(0, Math.Min((announcement.Name ?? "同密钥设备").Length, 80)));
						if (flag2)
						{
							Report("已发现同密钥设备");
							await _udp.SendAsync(AnnouncementBytes(), udpReceiveResult.RemoteEndPoint, token);
						}
					}
				}
				catch (CryptographicException)
				{
				}
				catch (JsonException)
				{
				}
				catch (SocketException)
				{
					Report("设备发现连接中断，正在重试");
					await Task.Delay(1000, token);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task AcceptLoopAsync(CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested)
			{
				TcpClient tcpClient = await _tcpListener.AcceptTcpClientAsync(token);
				if (!(tcpClient.Client.RemoteEndPoint is IPEndPoint iPEndPoint) || !SharedProtocol.IsLocalAddress(iPEndPoint.Address) || !_connections.Wait(0))
				{
					tcpClient.Dispose();
				}
				else
				{
					HandleClientAsync(tcpClient, token);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
		catch (SocketException)
		{
			if (!token.IsCancellationRequested)
			{
				Report("接收服务已停止，请重启应用");
			}
		}
	}

	private async Task HandleClientAsync(TcpClient client, CancellationToken token)
	{
		using (client)
		{
			using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
			_ = 1;
			try
			{
				deadline.CancelAfter(5000);
				NetworkStream stream = client.GetStream();
				byte[] header = new byte[4];
				await stream.ReadExactlyAsync(header, deadline.Token);
				int num = SharedProtocol.ReadFrameLength(header);
				if ((num < 28 || num > 1581056) ? true : false)
				{
					return;
				}
				byte[] frame = new byte[num];
				await stream.ReadExactlyAsync(frame, deadline.Token);
				ClipboardMessage message = JsonSerializer.Deserialize<ClipboardMessage>(SharedProtocol.Decrypt(_key, frame));
				if (!_enabled || (object)message == null || message.Type != "clipboard" || message.OriginDeviceId == _deviceId || string.IsNullOrEmpty(message.MessageId) || message.MessageId.Length > 64 || string.IsNullOrEmpty(message.OriginDeviceId) || message.OriginDeviceId.Length > 64 || string.IsNullOrEmpty(message.Text) || Encoding.UTF8.GetByteCount(message.Text) > 262144 || !SharedProtocol.IsFresh(message.TimestampUtc) || !RememberMessage(message.MessageId))
				{
					return;
				}
				_dispatchToUi(() =>
				{
					if (_enabled && !_disposed)
					{
						RemoteTextReceived?.Invoke(message.Text);
					}
				});
			}
			catch (Exception ex) when ((ex is IOException || ex is SocketException || ex is CryptographicException || ex is JsonException || ex is OperationCanceledException) ? true : false)
			{
				if (!token.IsCancellationRequested)
				{
					Report("一条消息未能接收，请检查配对和网络");
				}
			}
			finally
			{
				_connections.Release();
			}
		}
	}

	private async Task SendLoopAsync(CancellationToken token)
	{
		try
		{
			await foreach (ClipboardMessage message in _outbound.Reader.ReadAllAsync(token))
			{
				if (!_enabled || !SharedProtocol.IsFresh(message.TimestampUtc))
				{
					continue;
				}
				IPEndPoint[] array = (from p in _peers.Values
					where p.Manual || DateTimeOffset.UtcNow - p.LastSeen <= TimeSpan.FromSeconds(15.0)
					select p.EndPoint).Distinct().ToArray();
				if (array.Length == 0)
				{
					Report("未发现设备，本次内容未发送");
					continue;
				}
				int num = (await Task.WhenAll(array.Select((IPEndPoint endpoint) => SendAsync(endpoint, message, token)))).Count((bool ok) => ok);
				Report((num > 0) ? $"已发送至 {num} 个设备地址" : "发送失败，请检查网络和防火墙");
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task<bool> SendAsync(IPEndPoint endpoint, ClipboardMessage message, CancellationToken token)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
		deadline.CancelAfter(4000);
		try
		{
			if (!_enabled)
			{
				return false;
			}
			using TcpClient client = new TcpClient(AddressFamily.InterNetwork);
			await client.ConnectAsync(endpoint, deadline.Token);
			if (!_enabled)
			{
				return false;
			}
			byte[] array = SharedProtocol.Frame(SharedProtocol.Encrypt(_key, SharedProtocol.Serialize(message)));
			await client.GetStream().WriteAsync(array, deadline.Token);
			return true;
		}
		catch (Exception ex) when ((ex is IOException || ex is SocketException || ex is OperationCanceledException) ? true : false)
		{
			return false;
		}
	}

	private bool RememberMessage(string messageId)
	{
		long num = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		foreach (KeyValuePair<string, long> seenMessage in _seenMessages)
		{
			if (num - seenMessage.Value > 600000)
			{
				_seenMessages.TryRemove(seenMessage.Key, out var _);
			}
		}
		if (_seenMessages.Count < 2048)
		{
			return _seenMessages.TryAdd(messageId, num);
		}
		return false;
	}

	private void Report(string status)
	{
		_dispatchToUi(() =>
		{
			if (!_disposed)
			{
				StatusChanged?.Invoke(status);
			}
		});
	}

	private static string Hash(string text)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_enabled = false;
			_outbound.Writer.TryComplete();
			_stop.Cancel();
			_tcpListener.Stop();
			_udp.Dispose();
		}
	}
}
