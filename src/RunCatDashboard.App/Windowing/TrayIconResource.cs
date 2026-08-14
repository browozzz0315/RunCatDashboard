using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RunCatDashboard.App.Windowing;

internal interface ITrayIconResource : IDisposable
{
    Icon Icon { get; }
}

internal interface ITrayIconResourceLoader
{
    ITrayIconResource Load();
}

internal interface ITrayAnimationIconResourceLoader
{
    IReadOnlyList<ITrayIconResource> LoadFrames();
}

internal sealed class AssemblyTrayIconResourceLoader : ITrayIconResourceLoader
{
    internal const string ResourceName =
        "RunCatDashboard.App.Assets.RunCat.RunCatDashboard.Tray.ico";
    internal const string WhiteResourceName =
        "RunCatDashboard.App.Assets.RunCat.RunCatDashboard.Tray.White.ico";

    private readonly Assembly _assembly;
    private readonly string _resourceName;

    internal AssemblyTrayIconResourceLoader()
        : this(typeof(AssemblyTrayIconResourceLoader).Assembly, ResourceName)
    {
    }

    internal AssemblyTrayIconResourceLoader(Assembly assembly)
        : this(assembly, ResourceName)
    {
    }

    internal AssemblyTrayIconResourceLoader(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        _assembly = assembly;
        _resourceName = resourceName;
    }

    public ITrayIconResource Load()
    {
        return LoadResource(_assembly, _resourceName);
    }

    internal static ITrayIconResource LoadResource(
        Assembly assembly,
        string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Tray icon assembly resource '{resourceName}' was not found.");
        }

        try
        {
            using var loaded = new Icon(stream);
            return new OwnedTrayIconResource((Icon)loaded.Clone());
        }
        catch (Exception exception) when (
            exception is ArgumentException or ExternalException)
        {
            throw new InvalidOperationException(
                $"Tray icon assembly resource '{resourceName}' is invalid.",
                exception);
        }
    }
}

internal sealed class AssemblyTrayAnimationIconResourceLoader
    : ITrayAnimationIconResourceLoader
{
    internal const int FrameCount = 8;
    internal const string ResourceNamePrefix =
        "RunCatDashboard.App.Assets.RunCat.TrayAnimation.tray-cat-frame-";
    internal const string WhiteResourceNamePrefix =
        "RunCatDashboard.App.Assets.RunCat.TrayAnimation.White.tray-cat-frame-";

    private readonly Assembly _assembly;
    private readonly string _resourceNamePrefix;

    internal AssemblyTrayAnimationIconResourceLoader()
        : this(
            typeof(AssemblyTrayAnimationIconResourceLoader).Assembly,
            ResourceNamePrefix)
    {
    }

    internal AssemblyTrayAnimationIconResourceLoader(Assembly assembly)
        : this(assembly, ResourceNamePrefix)
    {
    }

    internal AssemblyTrayAnimationIconResourceLoader(
        Assembly assembly,
        string resourceNamePrefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceNamePrefix);
        _assembly = assembly;
        _resourceNamePrefix = resourceNamePrefix;
    }

    public IReadOnlyList<ITrayIconResource> LoadFrames()
    {
        var frames = new List<ITrayIconResource>(FrameCount);
        try
        {
            for (int index = 0; index < FrameCount; index++)
            {
                string resourceName = $"{_resourceNamePrefix}{index + 1:D2}.ico";
                frames.Add(
                    AssemblyTrayIconResourceLoader.LoadResource(
                        _assembly,
                        resourceName));
            }

            return frames.AsReadOnly();
        }
        catch
        {
            foreach (ITrayIconResource frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }
    }
}

internal sealed class OwnedTrayIconResource : ITrayIconResource
{
    private bool _isDisposed;

    internal OwnedTrayIconResource(Icon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        Icon = icon;
    }

    public Icon Icon { get; }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Icon.Dispose();
    }
}
