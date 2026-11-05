using System.Net;
using System.Security.Cryptography;
using System.Text;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using Xunit;

public class ModManagerTests
{
    [Fact]
    public async Task LocalModsCanBeListedDisabledAndRemovedWithoutTouchingConfig()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            var source=Path.Combine(dir,"local.jar"); await File.WriteAllTextAsync(source,"jar");
            var instance=new GameInstance{InstanceDirectory=Path.Combine(dir,"instance"),Loader=LoaderType.Fabric};
            var manager=new InstanceModManager(new ModrinthApi(new HttpClient()),new HttpClient());
            await manager.AddLocalAsync(instance,[source]);
            var mod=Assert.Single(manager.List(instance)); Assert.True(mod.Enabled);
            manager.SetEnabled(instance,mod,false); mod=Assert.Single(manager.List(instance)); Assert.False(mod.Enabled);
            Directory.CreateDirectory(Path.Combine(instance.InstanceDirectory,"config")); var config=Path.Combine(instance.InstanceDirectory,"config/a.txt");await File.WriteAllTextAsync(config,"keep");
            manager.Remove(instance,mod); Assert.Empty(manager.List(instance)); Assert.True(File.Exists(config));
        }finally{Directory.Delete(dir,true);}
    }
    [Fact]
    public async Task VanillaRejectsModInstallation()
    {
        var manager=new InstanceModManager(new ModrinthApi(new HttpClient()),new HttpClient());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>manager.AddLocalAsync(new GameInstance{Loader=LoaderType.Vanilla},["anything.jar"]));
    }
    [Fact]
    public async Task DependencyCyclesInstallOnceAndFailedDownloadsRollback()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        try
        {
            var payload=Encoding.UTF8.GetBytes("jar"); var hash=Convert.ToHexString(SHA512.HashData(payload));
            bool corrupt=true;
            var http=new HttpClient(new ModrinthTests.FakeHttp(r=>
            {
                if(r.RequestUri!.AbsolutePath=="/v2/version/b")return ModrinthTests.Json($$$"""{"id":"b","project_id":"dep","version_number":"1","game_versions":["1.21.1"],"loaders":["fabric"],"dependencies":[{"project_id":"root","version_id":"a","dependency_type":"required"}],"files":[{"primary":true,"filename":"dep.jar","url":"https://example.org/dep.jar","hashes":{"sha512":"{{{hash}}}"}}]}""");
                return new(HttpStatusCode.OK){Content=new ByteArrayContent(corrupt?Encoding.UTF8.GetBytes("bad"):payload)};
            }));
            var manager=new InstanceModManager(new ModrinthApi(http),http);
            var instance=new GameInstance{InstanceDirectory=dir,GameVersion="1.21.1",Loader=LoaderType.Fabric};
            var version=new ModrinthVersion("a","root","1",["1.21.1"],["fabric"],"https://example.org/root.jar","root.jar",hash,null,[new("dep","b","required")]);
            await Assert.ThrowsAsync<InvalidDataException>(()=>manager.InstallAsync(instance,version));
            Assert.Empty(manager.List(instance));
            corrupt=false; await manager.InstallAsync(instance,version); Assert.Equal(2,manager.List(instance).Count);
            await manager.InstallAsync(instance,version); Assert.Equal(2,manager.List(instance).Count);
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
    [Fact]
    public void RemovalCannotEscapeModsFolder()
    {
        var manager=new InstanceModManager(new ModrinthApi(new HttpClient()),new HttpClient());
        Assert.Throws<InvalidDataException>(()=>manager.Remove(new GameInstance{InstanceDirectory=Path.GetTempPath()},new InstalledMod("../outside.jar","outside",true,null,null)));
    }
}
