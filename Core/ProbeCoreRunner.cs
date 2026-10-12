using System;
using System.Threading.Tasks;
using ElyProxy.Models;

namespace ElyProxy.Core;

/// <summary>
/// Starts a temporary local SOCKS/mixed core for latency probes, choosing
/// Xray or sing-box based on the node protocol, and tears it down on dispose.
/// </summary>
public sealed class ProbeCoreRunner : IAsyncDisposable
{
	private readonly XrayManager? _xray;
	private readonly SingBoxProxyManager? _singBox;

	private ProbeCoreRunner(XrayManager? xray, SingBoxProxyManager? singBox)
	{
		_xray = xray;
		_singBox = singBox;
	}

	public static async Task<ProbeCoreRunner> StartAsync(VlessNode node, int port, XrayTunOptions options, bool? useSingBox = null)
	{
		if (useSingBox != true && ProxyProtocolSupport.SupportsXray(node))
		{
			XrayManager xray = new XrayManager(new ProcessManager());
			try
			{
				await xray.StartAsync(node, port, options, includeTunInbound: false).ConfigureAwait(false);
				return new ProbeCoreRunner(xray, null);
			}
			catch
			{
				await xray.StopAsync().ConfigureAwait(false);
				xray.Dispose();
				throw;
			}
		}
		if (useSingBox != false && ProxyProtocolSupport.SupportsSingBox(node))
		{
			SingBoxProxyManager singBox = new SingBoxProxyManager(new ProcessManager());
			try
			{
				await singBox.StartAsync(node, port, options).ConfigureAwait(false);
				return new ProbeCoreRunner(null, singBox);
			}
			catch
			{
				await singBox.StopAsync().ConfigureAwait(false);
				singBox.Dispose();
				throw;
			}
		}
		throw new NotSupportedException($"Протокол {node.ProtocolDisplay} не поддерживается локальными ядрами для проверки.");
	}

	public async ValueTask DisposeAsync()
	{
		if (_xray != null)
		{
			await _xray.StopAsync().ConfigureAwait(false);
			_xray.Dispose();
		}
		if (_singBox != null)
		{
			await _singBox.StopAsync().ConfigureAwait(false);
			_singBox.Dispose();
		}
	}
}
