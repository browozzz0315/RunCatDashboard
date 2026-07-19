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
        using Stream? stream = _assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Tray icon assembly resource '{ResourceName}' was not found.");
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
                $"Tray icon assembly resource '{ResourceName}' is invalid.",
                exception);
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
