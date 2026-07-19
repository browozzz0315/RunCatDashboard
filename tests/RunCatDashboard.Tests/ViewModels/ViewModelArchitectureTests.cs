using System.Reflection;
using System.Runtime.InteropServices;
using RunCatDashboard.App.ViewModels;

namespace RunCatDashboard.Tests.ViewModels;

public sealed class ViewModelArchitectureTests
{
    [Fact]
    public void MainWindowViewModel_HasNoWpfWindowHwndSourceOrPInvokeDependency()
    {
        Type viewModelType = typeof(MainWindowViewModel);
        Type[] referencedTypes = viewModelType
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Concat(viewModelType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.PropertyType))
            .Concat(viewModelType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
            .ToArray();

        Assert.DoesNotContain(referencedTypes, type => type.FullName == "System.Windows.Window");
        Assert.DoesNotContain(referencedTypes, type => type.FullName == "System.Windows.Interop.HwndSource");
        Assert.DoesNotContain(referencedTypes, type => type == typeof(nint));
        Assert.DoesNotContain(
            viewModelType.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            method => method.GetCustomAttribute<DllImportAttribute>() is not null);
    }
}
