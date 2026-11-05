using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CitrineLauncher.Handlers;
using CitrineLauncher.Models;
using Xunit;

public class PackTests
{
    [Fact]
    public void ManifestPreservesFabricAndMinecraftVersions()
    {
        var manifest = PackManifest.Parse("""{"formatVersion":1,"game":"minecraft","name":"Test","dependencies":{"minecraft":"1.20.1","fabric-loader":"0.15.11"},"files":[]}""");
        Assert.Equal("1.20.1",manifest.GameVersion); Assert.Equal("0.15.11",manifest.LoaderVersion);
    }
    [Theory]
    [InlineData("forge")][InlineData("neoforge")][InlineData("quilt-loader")]
    public void UnsupportedLoaderIsRejected(string loader) => Assert.Throws<InvalidDataException>(() => PackManifest.Parse($$"""{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.20.1","{{loader}}":"1"},"files":[]}"""));
    [Fact]
    public async Task PackIncludesClientFilesAndClientOverridesWin()
    {
        var dir = Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            var payload = Encoding.UTF8.GetBytes("valid"); var sha=Convert.ToHexString(SHA512.HashData(payload));
            var archive=Path.Combine(dir,"pack.mrpack");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
            {
                Write(zip,"modrinth.index.json",$$"""{"formatVersion":1,"game":"minecraft","name":"Test","dependencies":{"minecraft":"1.20.1","fabric-loader":"0.15.11"},"files":[{"path":"resourcepacks/test.zip","hashes":{"sha512":"{{sha}}"},"downloads":["https://example.org/file"]},{"path":"mods/server.jar","env":{"client":"unsupported"},"hashes":{"sha512":"{{sha}}"},"downloads":["https://example.org/server"]}]}""");
                Write(zip,"overrides/config/test.txt","common"); Write(zip,"client-overrides/config/test.txt","client");
            }
            var instance = new GameInstance {InstanceDirectory=Path.Combine(dir,"instance"),GameVersion="1.20.1",Loader=LoaderType.Fabric,LoaderVersion="0.15.11"};
            var http = new HttpClient(new ModrinthTests.FakeHttp(_ => new(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(payload)}));
            var result=await new PackInstaller(http).ImportAsync(archive,instance);
            Assert.True(result.Success,result.Message);
            Assert.Equal("client",await File.ReadAllTextAsync(Path.Combine(instance.InstanceDirectory,"config/test.txt")));
            Assert.True(File.Exists(Path.Combine(instance.InstanceDirectory,"resourcepacks/test.zip")));
            Assert.False(File.Exists(Path.Combine(instance.InstanceDirectory,"mods/server.jar")));
        }
        finally{Directory.Delete(dir,true);}
    }
    [Fact]
    public async Task FailedPackDoesNotPublishFiles()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            var archive=Path.Combine(dir,"pack.mrpack");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)) Write(zip,"modrinth.index.json","""{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.20.1"},"files":[{"path":"../escape","hashes":{"sha512":"bad"},"downloads":["https://example.org/file"]}]}""");
            var target=Path.Combine(dir,"instance");
            var result=await new PackInstaller(new HttpClient()).ImportAsync(archive,new GameInstance{InstanceDirectory=target,GameVersion="1.20.1"});
            Assert.False(result.Success); Assert.False(Directory.Exists(target));
        }
        finally{Directory.Delete(dir,true);}
    }
    private static void Write(ZipArchive zip,string path,string content){using var writer=new StreamWriter(zip.CreateEntry(path).Open());writer.Write(content);}
}
