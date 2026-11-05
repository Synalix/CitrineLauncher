using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CitrineLauncher;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using System.Net;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(UiAppBuilder))]
public class UiAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false });
}
public class UiTests
{
    [AvaloniaFact]
    public void SettingsFitsConstrainedHostAndCloseButtonIsReachable()
    {
        var host=new Window{Width=650,Height=330};
        var panel=new SettingsPanel();host.Content=panel;host.Show();host.UpdateLayout();
        Assert.True(panel.Bounds.Width<=host.ClientSize.Width);
        Assert.True(panel.Bounds.Height<=host.ClientSize.Height);
        var close=panel.FindControl<Button>("CloseButton")!;
        var origin=close.TranslatePoint(default,host)!.Value;
        var bounds=new Rect(origin,close.Bounds.Size);
        Assert.True(bounds.Right<=host.ClientSize.Width);Assert.True(bounds.Bottom<=host.ClientSize.Height);
        Assert.True(close.IsEffectivelyVisible);Capture(host,"settings-narrow");host.Close();
    }
    [AvaloniaFact]
    public void EmptyModListHasNamedActionsAndFitsMinimumWindow()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        var window=new ModsDialog(new GameInstance{Name="Long instance "+new string('x',100),GameVersion="1.21.1",Loader=LoaderType.Fabric,InstanceDirectory=path}){Width=540,Height=400};
        window.Show();window.UpdateLayout();
        Assert.True(window.FindControl<TextBlock>("EmptyText")!.IsVisible);
        Assert.False(window.FindControl<Button>("RemoveButton")!.IsEnabled);
        foreach(var button in window.GetVisualDescendants().OfType<Button>().Where(b=>b.IsEffectivelyVisible))
        {
            var origin=button.TranslatePoint(default,window)!.Value;
            Assert.True(origin.Y>=0);Assert.True(origin.Y+button.Bounds.Height<=window.ClientSize.Height+1);
        }
        Capture(window,"mods-narrow");window.Close();
    }
    [AvaloniaFact]
    public async Task SupersededSearchCannotReplaceNewResults()
    {
        var pending=new TaskCompletionSource<HttpResponseMessage>();
        var handler=new SearchHandler(pending);
        var http=new HttpClient(handler);
        var dialog=new ModrinthBrowseDialog(api:new ModrinthApi(http),http:http);dialog.Show();
        var box=dialog.FindControl<TextBox>("SearchBox")!;
        box.Text="new";
        dialog.FindControl<Button>("SearchButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(30);Dispatcher.UIThread.RunJobs();
        pending.TrySetResult(new(HttpStatusCode.OK){Content=new StringContent("{\"hits\":[],\"total_hits\":0}")});
        await Task.Delay(30);Dispatcher.UIThread.RunJobs();
        var items=dialog.FindControl<ListBox>("ResultsList")!.ItemsSource!.Cast<ModrinthResultItem>().ToList();
        Assert.Equal("Newest",Assert.Single(items).Title);Capture(dialog,"modrinth");dialog.Close();
    }
    [AvaloniaFact]
    public void ManualInstanceFormFitsMinimumSizeAndKeepsPackBrowseVisible()
    {
        // Seed cache to isolate layout from external version services.
        Settings.Instance.CachedGameVersions = new List<string> { "1.21.1" };
        Settings.Instance.GameVersionsCachedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dialog=new NewInstanceDialog(new CmlLib.Core.MinecraftLauncher()) { Width=440,Height=380 };
        dialog.Show();dialog.UpdateLayout();
        var browse=dialog.FindControl<Button>("BrowseModrinthButton")!;
        Assert.True(browse.IsEffectivelyVisible);
        var create=dialog.FindControl<Button>("CreateButton")!;
        var origin=create.TranslatePoint(default,dialog)!.Value;
        Assert.True(origin.Y+create.Bounds.Height<=dialog.ClientSize.Height);
        Assert.Equal("Mod loader",Avalonia.Automation.AutomationProperties.GetName(dialog.FindControl<ComboBox>("LoaderCombo")!));
        Capture(dialog,"new-instance-narrow");dialog.Close();
    }

    [AvaloniaFact]
    public void ConfirmLongMessageKeepsSafeActionReachable()
    {
        var dialog=new ConfirmDialog("Remove "+new string('x',150),new string('x',1000)){Width=360,Height=220};
        dialog.Show();dialog.UpdateLayout();
        var cancel=dialog.FindControl<Button>("CancelButton")!;
        var origin=cancel.TranslatePoint(default,dialog)!.Value;
        Assert.True(origin.Y+cancel.Bounds.Height<=dialog.ClientSize.Height);
        Assert.True(cancel.IsFocused);dialog.Close();
    }

    [AvaloniaFact]
    public void MainActionsAndLongStatusStayWithinMinimumWindow()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(path);
        Settings.Instance.CachedGameVersions=new List<string>{"1.21.1"};
        Settings.Instance.GameVersionsCachedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var previous=Settings.Instance.MinecraftPath;
        using var suppressed=Settings.Instance.SuppressSave();
        Settings.Instance.MinecraftPath=path;Settings.Instance.ParticlesEnabled=false;
        var window=new MainWindow{Width=720,Height=540};
        try
        {
            window.Show();window.FindControl<TextBlock>("lblStatus")!.Text=new string('x',500);window.UpdateLayout();
            foreach(var name in new[]{"btnLaunch","btnMods","cbInstances","accountCombo"})
            {
                var control=window.FindControl<Control>(name)!;
                var origin=control.TranslatePoint(default,window)!.Value;
                Assert.True(origin.X>=0);Assert.True(origin.Y>=0);
                Assert.True(origin.X+control.Bounds.Width<=window.ClientSize.Width+1);
                Assert.True(origin.Y+control.Bounds.Height<=window.ClientSize.Height+1);
            }
            Capture(window,"main-narrow");
        }
        finally{window.Close();Settings.Instance.MinecraftPath=previous;Directory.Delete(path,true);}
    }
    private static void Capture(Window window,string name)
    {
        var output=Environment.GetEnvironmentVariable("CITRINE_SCREENSHOTS_DIR");
        if(output==null)return;
        Directory.CreateDirectory(output);window.UpdateLayout();
        using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(output,name+".png"));
    }

    [AvaloniaFact]
    public void SettingsEscapeRequestsCloseWithoutPropagating()
    {
        var panel=new SettingsPanel();bool requested=false;
        panel.CloseRequested+=(_,_)=>requested=true;
        var args=new Avalonia.Input.KeyEventArgs{RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.Escape};
        panel.RaiseEvent(args);Assert.True(requested);Assert.True(args.Handled);
    }

    private sealed class SearchHandler(TaskCompletionSource<HttpResponseMessage> pending):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            // Deliberately ignore cancellation to exercise the stale-response guard.
            if(request.RequestUri!.Query.Contains("query=new"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("""{"hits":[{"project_id":"new","slug":"new","title":"Newest","description":"","versions":[],"downloads":1}],"total_hits":1}""")});
            return pending.Task;
        }
    }
}
