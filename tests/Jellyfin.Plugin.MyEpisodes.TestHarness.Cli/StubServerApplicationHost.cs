using System.Net;
using System.Reflection;
using MediaBrowser.Common;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MyEpisodes.TestHarness.Cli;

public class StubServerApplicationHost : IServerApplicationHost
{
    public string Name => "Jellyfin.Plugin.MyEpisodes.TestHarness.Cli";
    public System.Version ApplicationVersion => Version.Parse("1.0.0");

    public IEnumerable<Assembly> GetApiPluginAssemblies()
    {
        throw new NotImplementedException();
    }

    public void NotifyPendingRestart()
    {
        throw new NotImplementedException();
    }

    public IReadOnlyCollection<T> GetExports<T>(bool manageLifetime = true)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyCollection<T> GetExports<T>(CreationDelegateFactory defaultFunc, bool manageLifetime = true)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<Type> GetExportTypes<T>()
    {
        throw new NotImplementedException();
    }

    public T Resolve<T>()
    {
        throw new NotImplementedException();
    }

    public void Init(IServiceCollection serviceCollection)
    {
        throw new NotImplementedException();
    }

    public string SystemId { get; }
    public bool HasPendingRestart { get; }
    public bool ShouldRestart { get; set; }
    public IServiceProvider? ServiceProvider { get; set; }
    public string ApplicationVersionString { get; }
    public string ApplicationUserAgent { get; }
    public string ApplicationUserAgentAddress { get; }
    public event EventHandler? HasPendingRestartChanged;
    public string GetSmartApiUrl(HttpRequest request)
    {
        throw new NotImplementedException();
    }

    public string GetSmartApiUrl(IPAddress remoteAddr)
    {
        throw new NotImplementedException();
    }

    public string GetSmartApiUrl(string hostname)
    {
        throw new NotImplementedException();
    }

    public string GetApiUrlForLocalAccess(IPAddress ipAddress = null, bool allowHttps = true)
    {
        throw new NotImplementedException();
    }

    public string GetLocalApiUrl(string hostname, string scheme = null, int? port = null)
    {
        throw new NotImplementedException();
    }

    public string ExpandVirtualPath(string path)
    {
        throw new NotImplementedException();
    }

    public string ReverseVirtualPath(string path)
    {
        throw new NotImplementedException();
    }

    public bool CoreStartupHasCompleted { get; }
    public int HttpPort { get; }
    public int HttpsPort { get; }
    public bool ListenWithHttps { get; }
    public string FriendlyName { get; }
    public string RestoreBackupPath { get; set; }
}