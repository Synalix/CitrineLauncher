using System.Net;
using System.Security.Cryptography;
using System.Text;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using Xunit;

public class ConflictTests
{
    [Fact]
    public async Task DeclaredIncompatibleInstalledProjectBlocksInstallation()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        var data=Encoding.UTF8.GetBytes("jar");var hash=Convert.ToHexString(SHA512.HashData(data));
        var http=new HttpClient(new ModrinthTests.FakeHttp(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)}));
        var manager=new InstanceModManager(new ModrinthApi(http),http);
        var instance=new GameInstance{InstanceDirectory=dir,GameVersion="1.21.1",Loader=LoaderType.Fabric};
        try
        {
            var existing=new ModrinthVersion("a","existing","1",["1.21.1"],["fabric"],"https://example.org/a","a.jar",hash,null,[]);
            await manager.InstallAsync(instance,existing);
            var conflicting=new ModrinthVersion("b","new","1",["1.21.1"],["fabric"],"https://example.org/b","b.jar",hash,null,[new("existing",null,"incompatible")]);
            await Assert.ThrowsAsync<InvalidDataException>(()=>manager.InstallAsync(instance,conflicting));
            Assert.Single(manager.List(instance));
        }
        finally{Directory.Delete(dir,true);}
    }
}

public class DependencyResolutionTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task IncompatibilityIsIndependentOfInstallOrder(bool versionSpecific)
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        var data=Encoding.UTF8.GetBytes("jar");var hash=Convert.ToHexString(SHA512.HashData(data));
        var http=new HttpClient(new ModrinthTests.FakeHttp(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)}));
        var manager=new InstanceModManager(new ModrinthApi(http),http);
        var instance=new GameInstance{InstanceDirectory=dir,GameVersion="1.21.1",Loader=LoaderType.Fabric};
        try
        {
            var a=new ModrinthVersion("a","project-a","1",["1.21.1"],["fabric"],"https://example.org/a","a.jar",hash,null,[new("project-b",versionSpecific?"b":null,"incompatible")]);
            var b=new ModrinthVersion("b","project-b","1",["1.21.1"],["fabric"],"https://example.org/b","b.jar",hash,null,[]);
            await manager.InstallAsync(instance,a);
            await Assert.ThrowsAsync<InvalidDataException>(()=>manager.InstallAsync(instance,b));
            Assert.Single(manager.List(instance));
        }finally{Directory.Delete(dir,true);}
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task ExactDiamondDependencyReplacesProvisionalLatestRegardlessOfOrder(bool reverse)
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        var data=Encoding.UTF8.GetBytes("jar");var hash=Convert.ToHexString(SHA512.HashData(data));
        string Version(string id,string project,string dependencies="[]")=>System.Text.Json.JsonSerializer.Serialize(new{id,project_id=project,version_number=id,game_versions=new[]{"1.21.1"},loaders=new[]{"fabric"},dependencies=System.Text.Json.JsonSerializer.Deserialize<object>(dependencies),files=new[]{new{filename=id+".jar",url="https://example.org/"+id,primary=true,hashes=new{sha512=hash}}}});
        var http=new HttpClient(new ModrinthTests.FakeHttp(r=>
        {
            var path=r.RequestUri!.AbsolutePath;
            if(path=="/v2/project/B/version")return ModrinthTests.Json("["+Version("b2","B","""[{"project_id":"obsolete","version_id":"old-dep","dependency_type":"required"}]""")+","+Version("b1","B")+"]");
            if(path=="/v2/project/C/version")return ModrinthTests.Json("["+Version("c","C","""[{"project_id":"B","version_id":"b1","dependency_type":"required"}]""")+"]");
            if(path=="/v2/version/old-dep")return ModrinthTests.Json(Version("old-dep","obsolete"));
            if(path=="/v2/version/b1")return ModrinthTests.Json(Version("b1","B"));
            if(!path.StartsWith("/v2/"))return new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
            throw new InvalidOperationException("Unexpected request: "+path);
        }));
        var deps=new ModrinthDependency[]{new("B",null,"required"),new("C",null,"required")};if(reverse)Array.Reverse(deps);
        try
        {
            await new InstanceModManager(new ModrinthApi(http),http).InstallAsync(new GameInstance{InstanceDirectory=dir,GameVersion="1.21.1",Loader=LoaderType.Fabric},
                new ModrinthVersion("root","root","1",["1.21.1"],["fabric"],"https://example.org/root","root.jar",hash,null,deps));
            Assert.True(File.Exists(Path.Combine(dir,"mods/b1.jar")));Assert.False(File.Exists(Path.Combine(dir,"mods/b2.jar")));
            Assert.Equal(3,Directory.GetFiles(Path.Combine(dir,"mods"),"*.jar").Length);
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}

public class VersionSpecificConflictTests
{
    [Fact]
    public async Task VersionSpecificIncompatibilityAllowsOtherVersions()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        var data=Encoding.UTF8.GetBytes("jar");var hash=Convert.ToHexString(SHA512.HashData(data));
        var http=new HttpClient(new ModrinthTests.FakeHttp(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)}));
        var manager=new InstanceModManager(new ModrinthApi(http),http);
        var instance=new GameInstance{InstanceDirectory=dir,GameVersion="1.21.1",Loader=LoaderType.Fabric};
        try
        {
            await manager.InstallAsync(instance,new ModrinthVersion("a","A","1",["1.21.1"],["fabric"],"https://example.org/a","a.jar",hash,null,[new("B","b1","incompatible")]));
            await manager.InstallAsync(instance,new ModrinthVersion("b2","B","2",["1.21.1"],["fabric"],"https://example.org/b","b2.jar",hash,null,[]));
            Assert.Equal(2,manager.List(instance).Count);
        }finally{Directory.Delete(dir,true);}
    }
}

public class ContradictoryRequirementTests
{
    [Fact]
    public async Task ContradictoryExactVersionsDoNotPublishAnyMods()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        var hash=new string('0',128);
        var http=new HttpClient(new ModrinthTests.FakeHttp(r=>
        {
            var id=r.RequestUri!.Segments.Last();
            return ModrinthTests.Json(System.Text.Json.JsonSerializer.Serialize(new{id,project_id="B",version_number=id,game_versions=new[]{"1.21.1"},loaders=new[]{"fabric"},dependencies=Array.Empty<object>(),files=new[]{new{filename=id+".jar",url="https://example.org/"+id,primary=true,hashes=new{sha512=hash}}}}));
        }));
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>new InstanceModManager(new ModrinthApi(http),http).InstallAsync(
                new GameInstance{InstanceDirectory=dir,GameVersion="1.21.1",Loader=LoaderType.Fabric},
                new ModrinthVersion("root","root","1",["1.21.1"],["fabric"],"https://example.org/root","root.jar",hash,null,[new("B","b1","required"),new("B","b2","required")])));
            Assert.False(Directory.Exists(dir));
        }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
