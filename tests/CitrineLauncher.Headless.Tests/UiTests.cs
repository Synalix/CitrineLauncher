using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CitrineLauncher;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using System.Net;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization=true)]
[assembly: AvaloniaTestApplication(typeof(UiAppBuilder))]
public class UiAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()=>AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false});
}
public class UiTests
{
    internal static void SeedVersions()
    {
        Settings.Instance.CachedGameVersions=["1.21.1"];
        Settings.Instance.GameVersionsCachedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
    internal static Window Host(Control page,double width=650,double height=390)
    {
        var window=new Window{Width=width,Height=height,Content=page,Background=Avalonia.Media.Brushes.Black};
        window.Styles.Add(new StyleInclude(new Uri("avares://CitrineLauncher/")){Source=new Uri("avares://CitrineLauncher/Styles/Compact.axaml")});
        window.Show();window.UpdateLayout();return window;
    }
    internal static void AssertWithin(Control control,Window window)
    {
        var origin=control.TranslatePoint(default,window)!.Value;
        Assert.True(origin.X>=-1&&origin.Y>=-1,$"{control.Name} starts outside its host: {origin}");
        Assert.True(origin.X+control.Bounds.Width<=window.ClientSize.Width+1,$"{control.Name} overflows horizontally");
        Assert.True(origin.Y+control.Bounds.Height<=window.ClientSize.Height+1,$"{control.Name} overflows vertically");
    }
    internal static void Capture(Window window,string name)
    {
        var output=Environment.GetEnvironmentVariable("CITRINE_SCREENSHOTS_DIR");if(output==null)return;
        Directory.CreateDirectory(output);window.UpdateLayout();using var frame=window.CaptureRenderedFrame();frame?.Save(Path.Combine(output,name+".png"));
    }
    [AvaloniaFact]
    public void SettingsHasCompactContentRowsAndInlineAccountEditor()
    {
        using var suppressed=Settings.Instance.SuppressSave();
        var panel=new SettingsPanel();using var workspace=new ManagementWorkspace();
        var window=Host(workspace);workspace.Navigate("General");window.UpdateLayout();
        var current=Assert.IsType<SettingsPanel>(workspace.CurrentPage);
        Assert.InRange(current.FindControl<ComboBox>("MinRamCombo")!.Bounds.Width,90,150);
        Capture(window,"general-compact");
        workspace.Navigate("Accounts");window.UpdateLayout();
        current.FindControl<Button>("AddOfflineButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(current.FindControl<StackPanel>("AccountEditor")!.IsVisible);Assert.Empty(window.OwnedWindows);
        current.FindControl<TextBox>("UsernameInput")!.Text="bad name";
        current.FindControl<Button>("SaveAccountButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(current.FindControl<TextBlock>("AccountErrorLabel")!.IsVisible);Capture(window,"accounts-compact");window.Close();
    }
    [AvaloniaFact]
    public void EmptyModsPageFitsCompactHost()
    {
        using var view=new ModsView(new GameInstance{Name="Long instance "+new string('x',100),GameVersion="1.21.1",Loader=LoaderType.Fabric,InstanceDirectory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString())});
        var window=Host(view,480,330);
        Assert.True(view.FindControl<TextBlock>("EmptyText")!.IsVisible);Assert.False(view.FindControl<Button>("RemoveButton")!.IsEnabled);
        foreach(var button in view.GetVisualDescendants().OfType<Button>().Where(b=>b.IsEffectivelyVisible))AssertWithin(button,window);
        Capture(window,"mods-compact");window.Close();
    }
    [AvaloniaFact]
    public async Task SupersededSearchCannotReplaceNewResults()
    {
        var pending=new TaskCompletionSource<HttpResponseMessage>();
        using var http=new HttpClient(new SearchHandler(pending));
        using var page=new ModrinthBrowserView(api:new ModrinthApi(http),http:http);var window=Host(page,480,330);
        page.FindControl<TextBox>("SearchBox")!.Text="new";
        page.FindControl<Button>("SearchButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await WaitUntil(()=>page.FindControl<ListBox>("ResultsList")!.ItemCount==1);
        pending.TrySetResult(new(HttpStatusCode.OK){Content=new StringContent("{\"hits\":[],\"total_hits\":0}")});
        Dispatcher.UIThread.RunJobs();
        var items=page.FindControl<ListBox>("ResultsList")!.ItemsSource!.Cast<ModrinthResultItem>().ToList();Assert.Equal("Newest",Assert.Single(items).Title);
        Capture(window,"modrinth-compact");window.Close();
    }
    [AvaloniaFact]
    public void NewInstancePageKeepsActionsReachableAtCompactSize()
    {
        SeedVersions();using var page=new NewInstanceView();var window=Host(page,480,330);
        AssertWithin(page.FindControl<Button>("CreateButton")!,window);
        Assert.True(page.FindControl<Button>("BrowseModrinthButton")!.IsEffectivelyVisible);
        Assert.Equal("Mod loader",Avalonia.Automation.AutomationProperties.GetName(page.FindControl<ComboBox>("LoaderCombo")!));
        Capture(window,"new-instance-compact");window.Close();
    }
    [AvaloniaFact]
    public void ConfirmLongMessageKeepsSafeActionReachable()
    {
        var dialog=new ConfirmDialog("Remove "+new string('x',150),new string('x',1000)){Width=360,Height=220};dialog.Show();dialog.UpdateLayout();
        var cancel=dialog.FindControl<Button>("CancelButton")!;AssertWithin(cancel,dialog);Assert.True(cancel.IsFocused);dialog.Close();
    }
    [AvaloniaFact]
    public void MainNewInstanceActionUsesWorkspaceAndReturnsToUnchangedLauncher()
    {
        SeedVersions();var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(path);
        var previous=Settings.Instance.MinecraftPath;using var suppressed=Settings.Instance.SuppressSave();
        Settings.Instance.MinecraftPath=path;Settings.Instance.ParticlesEnabled=false;
        var window=new MainWindow{Width=720,Height=540};
        try
        {
            window.Show();window.UpdateLayout();Capture(window,"main-preserved");
            window.FindControl<Button>("btnNewInstance")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));window.UpdateLayout();
            var workspace=Assert.Single(window.GetVisualDescendants().OfType<ManagementWorkspace>());
            Assert.IsType<NewInstanceView>(workspace.CurrentPage);Assert.Empty(window.OwnedWindows);Capture(window,"workspace-main-narrow");
            workspace.RequestClose();window.UpdateLayout();Assert.Empty(window.GetVisualDescendants().OfType<ManagementWorkspace>());
            Assert.True(window.FindControl<Button>("btnSettings")!.IsFocused);
        }
        finally{window.Close();Settings.Instance.MinecraftPath=previous;Directory.Delete(path,true);}
    }
    [AvaloniaFact]
    public void SettingsEscapeRequestsCloseWithoutPropagating()
    {
        var panel=new SettingsPanel();bool requested=false;panel.CloseRequested+=(_,_)=>requested=true;
        var args=new Avalonia.Input.KeyEventArgs{RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.Escape};panel.RaiseEvent(args);
        Assert.True(requested);Assert.True(args.Handled);
    }
    internal static async Task WaitUntil(Func<bool> predicate)
    {
        var deadline=DateTime.UtcNow.AddSeconds(3);
        while(!predicate()&&DateTime.UtcNow<deadline){await Task.Delay(10);Dispatcher.UIThread.RunJobs();}
        Assert.True(predicate());
    }
    private sealed class SearchHandler(TaskCompletionSource<HttpResponseMessage> pending):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.RequestUri!.Query.Contains("query=new"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("""{"hits":[{"project_id":"new","slug":"new","title":"Newest","description":"A compact result with actual metadata.","versions":[],"downloads":1}],"total_hits":1}""")});
            return pending.Task;
        }
    }
}
