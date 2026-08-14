namespace RunCatDashboard.App.Theming;

public sealed class ThemeConfigurationException : Exception
{
    public ThemeConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
