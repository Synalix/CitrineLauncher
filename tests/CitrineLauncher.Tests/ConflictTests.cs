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
