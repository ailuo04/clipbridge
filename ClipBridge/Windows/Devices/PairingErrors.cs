using System;
using System.Net.Sockets;

namespace ClipBridge.Windows;

internal static class PairingErrors
{
    public static string Connect(Exception error) => error is OperationCanceledException or TimeoutException ||
        error is SocketException { SocketErrorCode: SocketError.TimedOut }
        ? "连接超时，请确认两台设备处于可以互访的同一网络"
        : "无法连接对方，请确认对方已打开互传且网络允许互访";
}