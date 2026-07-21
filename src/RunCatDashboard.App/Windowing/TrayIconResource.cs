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

    private readonly Assembly _assembly;

    internal AssemblyTrayIconResourceLoader()
        : this(typeof(AssemblyTrayIconResourceLoader).Assembly)
    {
    }

    internal AssemblyTrayIconResourceLoader(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assembly = assembly;
    }

    public ITrayIconResource Load()
    {
        return LoadResource(_assembly, ResourceName);
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

    private readonly Assembly _assembly;

    internal AssemblyTrayAnimationIconResourceLoader()
        : this(typeof(AssemblyTrayAnimationIconResourceLoader).Assembly)
    {
    }

    internal AssemblyTrayAnimationIconResourceLoader(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assembly = assembly;
    }

    public IReadOnlyList<ITrayIconResource> LoadFrames()
    {
        var frames = new List<ITrayIconResource>(FrameCount);
        try
        {
            for (int index = 0; index < FrameCount; index++)
            {
                string resourceName = $"{ResourceNamePrefix}{index + 1:D2}.ico";
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
