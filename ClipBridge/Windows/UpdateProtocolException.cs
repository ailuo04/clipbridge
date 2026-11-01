using System;

namespace ClipBridge.Windows;

internal sealed class UpdateProtocolException : Exception
{
	public UpdateProtocolException(string message)
		: base(message)
	{
	}

	public UpdateProtocolException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
