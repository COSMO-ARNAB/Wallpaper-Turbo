using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WallpaperTurbo.UI.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace WallpaperTurbo.UiTests;

/// <summary>
/// XAML load/apply tests that require a real WPF Application, because StaticResource
/// lookups inside ControlTemplates resolve against Application.Resources (never the
/// element chain). These live in their own test project: a created Application poisons
/// AppDomain-shared tests (Application.Current performs a cross-thread VerifyAccess).
/// A single dedicated STA thread hosts the Application and all control work, mirroring
/// the app's single UI thread.
/// </summary>
public class SettingsPageLoadsTests
{
    private static readonly Dispatcher UiDispatcher = CreateUiThread();
    private static Application? _app;

    private static Dispatcher CreateUiThread()
    {
        Exception? failure = null;
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            try
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        while (dispatcher == null && failure == null)
        {
            Thread.Sleep(10);
        }
        if (failure != null)
        {
            throw new InvalidOperationException("Failed to start UI thread.", failure);
        }
        return dispatcher!;
    }

    private static void RunOnUiThread(Action action) => UiDispatcher.Invoke(action);

    private static ResourceDictionary LoadDictionary(string relativePath)
    {
        var uri = new Uri($"/WallpaperTurbo.UI;component/{relativePath}", UriKind.Relative);
        return (ResourceDictionary)Application.LoadComponent(uri);
    }

    /// <summary>
    /// Mirrors App.xaml: WPF-UI theme/control dictionaries first, then the central
    /// theme dictionaries, all merged into Application.Resources. Created once per
    /// test class on the dedicated UI thread (an AppDomain allows a single
    /// Application instance).
    /// </summary>
    private static Application GetApp()
    {
        if (_app == null)
        {
            RunOnUiThread(() =>
            {
                if (_app != null)
                {
                    return;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var resources = new ResourceDictionary();
                resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
                resources.MergedDictionaries.Add(new ControlsDictionary());
                resources.MergedDictionaries.Add(LoadDictionary("Theme/NeonTechStyle.xaml"));
                resources.MergedDictionaries.Add(LoadDictionary("Theme/TechieStyles.xaml"));
                app.Resources = resources;
                _app = app;
            });
        }
        return _app!;
    }

    [Fact]
    public void SettingsView_LoadsWithoutXamlParseException()
    {
        RunOnUiThread(() =>
        {
            GetApp();

            var settingsView = new SettingsView();
            var window = new Window { Content = settingsView };

            // Materializing the view is where the invalid `FallbackValue={Binding}`
            // used to throw "A 'Binding' cannot be set on the 'FallbackValue' property",
            // preventing the Settings page (Theme / Performance Mode / GPU Preference
            // combo boxes) from ever rendering and flooding startup-diagnostics.log.
            settingsView.ApplyTemplate();
            settingsView.Measure(new Size(1240, 780));
            settingsView.UpdateLayout();
            window.UpdateLayout();
        });
    }

    [Fact]
    public void GlassComboBox_Template_AppliesWithoutXamlParseException()
    {
        RunOnUiThread(() =>
        {
            GetApp();
            var glass = LoadDictionary("Theme/GlassStyles.xaml");
            var style = (Style)glass["GlassComboBox"];

            var comboBox = new ComboBox { Style = style };
            comboBox.Items.Add(new ComboBoxItem { Content = "Balanced" });
            comboBox.Items.Add(new ComboBoxItem { Content = "High Performance" });
            comboBox.Items.Add(new ComboBoxItem { Content = "Power Saver" });
            comboBox.SelectedIndex = 0;

            var window = new Window { Content = comboBox };

            comboBox.ApplyTemplate();
            comboBox.Measure(new Size(200, 40));
            comboBox.UpdateLayout();
        });
    }
}
