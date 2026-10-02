using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Desktop.Tests.BuilderTestApplication))]

namespace Desktop.Tests;

public static class BuilderTestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<BuilderTestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia().WithInterFont();
}

public sealed class BuilderTestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
