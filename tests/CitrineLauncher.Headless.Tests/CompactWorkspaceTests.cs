using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using CitrineLauncher;
using CitrineLauncher.Handlers;
using Xunit;

public class CompactWorkspaceTests
{
    [AvaloniaFact]
    public void CreationAndSettingsUseTheSameHostWithoutOpeningWindows()
    {
        Settings.Instance.CachedGameVersions=["1.21.1"];
        Settings.Instance.GameVersionsCachedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var workspace=new ManagementWorkspace();var window=new Window{Width=650,Height=390,Content=workspace};window.Show();
        workspace.Navigate("New instance");window.UpdateLayout();
        Assert.IsType<NewInstanceView>(workspace.CurrentPage);
        var create=((NewInstanceView)workspace.CurrentPage!).FindControl<Button>("CreateButton")!;
        Assert.InRange(create.Bounds.Width,50,105);Assert.InRange(create.Bounds.Height,24,32);
        Assert.Empty(window.OwnedWindows);
        workspace.Navigate("General");window.UpdateLayout();
        Assert.IsType<SettingsPanel>(workspace.CurrentPage);Assert.Empty(window.OwnedWindows);
        var combo=((SettingsPanel)workspace.CurrentPage!).FindControl<ComboBox>("MinRamCombo")!;
        Assert.InRange(combo.Bounds.Width,90,160);
        window.Close();workspace.Dispose();
    }
}

public class WorkspaceNavigationTests
{
    [AvaloniaFact]
    public void BrowseBackPreservesTheCreationForm()
    {
        UiTests.SeedVersions();
        using var http=new System.Net.Http.HttpClient(new EmptySearch());
        using var workspace=new ManagementWorkspace(api:new ModrinthApi(http),http:http);
        var window=UiTests.Host(workspace);workspace.Navigate("New instance");
        var form=Assert.IsType<NewInstanceView>(workspace.CurrentPage);form.FindControl<TextBox>("NameInput")!.Text="Adventure";
        form.FindControl<Button>("BrowseModrinthButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.IsType<ModrinthBrowserView>(workspace.CurrentPage);Assert.Empty(window.OwnedWindows);
        workspace.Navigate("New instance");Assert.Same(form,workspace.CurrentPage);Assert.Equal("Adventure",form.FindControl<TextBox>("NameInput")!.Text);
        window.Close();
    }
    [AvaloniaFact]
    public void RunningOperationBlocksCategoryChangesAndRequestsCancellationOnReturn()
    {
        using var workspace=new ManagementWorkspace();var window=UiTests.Host(workspace);
        var operation=new BusyPage();workspace.FindControl<ContentControl>("PageHost")!.Content=operation;
        Assert.False(workspace.Navigate("General"));Assert.Same(operation,workspace.CurrentPage);
        bool closed=false;workspace.CloseRequested+=(_,_)=>closed=true;
        Assert.False(workspace.RequestClose());Assert.True(operation.Cancelled);Assert.False(closed);
        operation.Finish();Assert.True(workspace.RequestClose());Assert.True(closed);window.Close();
    }
    [AvaloniaFact]
    public void VanillaCreationReturnsToInstancesAndPublishesSelectionWithoutAWindow()
    {
        UiTests.SeedVersions();var previous=Settings.Instance.MinecraftPath;
        var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString());
        using var suppressed=Settings.Instance.SuppressSave();Settings.Instance.MinecraftPath=dir;InstanceManager.Instance.Load();
        using var workspace=new ManagementWorkspace();var window=UiTests.Host(workspace);CitrineLauncher.Models.GameInstance? created=null;
        workspace.InstanceCreated+=i=>created=i;
        try
        {
            workspace.Navigate("New instance");var form=Assert.IsType<NewInstanceView>(workspace.CurrentPage);
            form.FindControl<TextBox>("NameInput")!.Text="Adventure";
            form.FindControl<Button>("CreateButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(created);Assert.Equal("Adventure",created.Name);Assert.IsType<InstancesView>(workspace.CurrentPage);
            Assert.Same(created,workspace.FindControl<ComboBox>("InstanceSelector")!.SelectedItem);Assert.Empty(window.OwnedWindows);
        }
        finally{window.Close();Settings.Instance.MinecraftPath=previous;InstanceManager.Instance.Load();if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
    }
    [AvaloniaFact]
    public void RunningGameCannotReachMutationCategories()
    {
        using var workspace=new ManagementWorkspace();var window=UiTests.Host(workspace);workspace.Navigate("General");workspace.SetCanEdit(false);
        foreach(var category in new[]{"Instances","New instance","Mods","Modrinth","Performance"})Assert.False(workspace.Navigate(category));
        Assert.IsType<SettingsPanel>(workspace.CurrentPage);window.Close();
    }
    private sealed class BusyPage:WorkspacePage
    {
        public bool Cancelled{get;private set;}
        public BusyPage()=>IsBusy=true;
        public override void CancelOperation()=>Cancelled=true;
        public void Finish()=>IsBusy=false;
    }
    private sealed class EmptySearch:System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken ct)=>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent("{\"hits\":[],\"total_hits\":0}")});
    }
}

public class InlineAccountTests
{
    [AvaloniaFact]
    public void OfflineAddAndRenameKeepIdentityWithoutNewWindows()
    {
        using var suppressed=Settings.Instance.SuppressSave();var previous=Settings.Instance.Username;
        using var workspace=new ManagementWorkspace();var window=UiTests.Host(workspace);workspace.Navigate("Accounts");
        var page=Assert.IsType<SettingsPanel>(workspace.CurrentPage);CitrineLauncher.Handlers.Account? added=null;
        try
        {
            page.FindControl<Button>("AddOfflineButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            page.FindControl<TextBox>("UsernameInput")!.Text="Test_"+Guid.NewGuid().ToString("N")[..6];
            page.FindControl<Button>("SaveAccountButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            added=Settings.Instance.Accounts.Last();var id=added.Id;Settings.Instance.Username=added.Username;
            page.FindControl<ListBox>("AccountsList")!.SelectedItem=added;
            page.FindControl<Button>("EditAccountButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            page.FindControl<TextBox>("UsernameInput")!.Text="Renamed_"+Guid.NewGuid().ToString("N")[..6];
            page.FindControl<Button>("SaveAccountButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(id,added.Id);Assert.Equal(added.Username,Settings.Instance.Username);Assert.Empty(window.OwnedWindows);
        }
        finally{if(added!=null){OfflineSkinServer.Shared.Unregister(added);Settings.Instance.Accounts.Remove(added);}Settings.Instance.Username=previous;window.Close();}
    }
}

public class WorkspaceVisualTests
{
    [AvaloniaFact]
    public void CategoryLayoutsKeepActionsInOneFrameAtSupportedSizes()
    {
        UiTests.SeedVersions();var previous=Settings.Instance.MinecraftPath;
        var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString());
        using var suppressed=Settings.Instance.SuppressSave();Settings.Instance.MinecraftPath=dir;InstanceManager.Instance.Load();
        var vanilla=InstanceManager.Instance.CreateInstance("Vanilla 1.21.1","1.21.1",CitrineLauncher.Models.LoaderType.Vanilla,"");
        var fabric=InstanceManager.Instance.CreateInstance("Fabric 1.21.1","1.21.1",CitrineLauncher.Models.LoaderType.Fabric,"0.16.10");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(fabric.InstanceDirectory,"mods"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(fabric.InstanceDirectory,"mods","example-client-mod.jar"),"test fixture");
        using var workspace=new ManagementWorkspace(fabric);var window=UiTests.Host(workspace,850,460);
        try
        {
            foreach(var size in new[]{new Size(850,460),new Size(650,374)})
            {
                window.Width=size.Width;window.Height=size.Height;
                foreach(var category in new[]{"Instances","New instance","Mods","General","Appearance","Performance","Accounts"})
                {
                    Assert.True(workspace.Navigate(category));window.UpdateLayout();
                    foreach(var button in workspace.GetVisualDescendants().OfType<Button>().Where(b=>b.IsEffectivelyVisible))
                    {
                        // Content in ScrollViewer can legitimately lie outside its viewport; assert the fixed shell actions.
                        if(button.Name=="ReturnButton")UiTests.AssertWithin(button,window);
                    }
                    Assert.Empty(window.OwnedWindows);UiTests.Capture(window,$"workspace-{category.Replace(' ','-').ToLowerInvariant()}-{size.Width}");
                }
            }
        }
        finally{window.Close();Settings.Instance.MinecraftPath=previous;InstanceManager.Instance.Load();if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
    }
}

public class EmbeddedPackFlowTests
{
    [AvaloniaFact]
    public async Task PackSelectionReturnsToCreationAndCleansOwnedDownload()
    {
        UiTests.SeedVersions();var previous=Settings.Instance.MinecraftPath;
        var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString());
        using var suppressed=Settings.Instance.SuppressSave();Settings.Instance.MinecraftPath=dir;InstanceManager.Instance.Load();
        byte[] pack;
        using(var buffer=new System.IO.MemoryStream())
        {
            using(var archive=new System.IO.Compression.ZipArchive(buffer,System.IO.Compression.ZipArchiveMode.Create,true))
            using(var writer=new System.IO.StreamWriter(archive.CreateEntry("modrinth.index.json").Open()))
                writer.Write("""{"formatVersion":1,"game":"minecraft","name":"Test pack","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16.10"},"files":[]}""");
            pack=buffer.ToArray();
        }
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(pack));
        using var http=new System.Net.Http.HttpClient(new PackHttp(pack,hash));
        using var workspace=new ManagementWorkspace(api:new ModrinthApi(http),http:http);
        var window=UiTests.Host(workspace);CitrineLauncher.Models.GameInstance? created=null;workspace.InstanceCreated+=i=>created=i;
        try
        {
            workspace.Navigate("Modrinth");var browser=Assert.IsType<ModrinthBrowserView>(workspace.CurrentPage);
            await UiTests.WaitUntil(()=>browser.FindControl<ListBox>("ResultsList")!.ItemCount==1);
            browser.FindControl<ListBox>("ResultsList")!.SelectedIndex=0;
            await UiTests.WaitUntil(()=>browser.FindControl<ComboBox>("VersionCombo")!.SelectedItem!=null);
            browser.FindControl<Button>("ImportButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await UiTests.WaitUntil(()=>workspace.CurrentPage is NewInstanceView form&&form.FindControl<TextBox>("ModpackPathBox")!.Text=="Test pack");
            var form=Assert.IsType<NewInstanceView>(workspace.CurrentPage);
            Assert.False(form.FindControl<ComboBox>("LoaderCombo")!.IsEnabled);
            form.FindControl<Button>("CreateButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await UiTests.WaitUntil(()=>created!=null);
            Assert.IsType<InstancesView>(workspace.CurrentPage);Assert.Equal("0.16.10",created!.LoaderVersion);Assert.Empty(window.OwnedWindows);
        }
        finally{window.Close();Settings.Instance.MinecraftPath=previous;InstanceManager.Instance.Load();if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
    }
    private sealed class PackHttp(byte[] bytes,string hash):System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken ct)
        {
            string json;
            if(request.RequestUri!.AbsolutePath=="/pack.mrpack")return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.ByteArrayContent(bytes)});
            if(request.RequestUri.AbsolutePath.EndsWith("/version"))json=System.Text.Json.JsonSerializer.Serialize(new[]{new{id="v1",project_id="p1",version_number="1",game_versions=new[]{"1.21.1"},loaders=new[]{"fabric"},dependencies=Array.Empty<object>(),files=new[]{new{filename="pack.mrpack",url="https://example.org/pack.mrpack",primary=true,hashes=new{sha512=hash}}}}});
            else json="""{"hits":[{"project_id":"p1","slug":"p1","title":"Test pack","description":"Pack flow fixture","versions":["1.21.1"],"downloads":1}],"total_hits":1}""";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent(json)});
        }
    }
}
