using System.Net;
using System.Security.Cryptography;
using System.Text;
using CitrineLauncher.Handlers;
using Xunit;

public class ModrinthTests
{
    internal sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
    internal static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    [Fact]
    public async Task SearchFiltersByTypeLoaderVersionAndOffset()
    {
        string uri = "";
        var api = new ModrinthApi(new HttpClient(new FakeHttp(r => { uri = Uri.UnescapeDataString(r.RequestUri!.ToString()); return Json("{\"hits\":[],\"total_hits\":0}"); })));
        await api.SearchAsync("sodium", "1.21.1", "mod", "fabric", 20);
        Assert.Contains("project_type:mod", uri); Assert.Contains("categories:fabric", uri);
        Assert.Contains("versions:1.21.1", uri); Assert.Contains("offset=20", uri);
    }
    [Fact]
    public async Task VersionUsesPrimaryJarAndParsesRequiredDependency()
    {
        var api = new ModrinthApi(new HttpClient(new FakeHttp(_ => Json("""
        [{"id":"v1","project_id":"p1","version_number":"1.0","game_versions":["1.21.1"],"loaders":["fabric"],
        "dependencies":[{"project_id":"dep","version_id":null,"dependency_type":"required"}],
        "files":[{"filename":"sources.jar","url":"https://example.org/s.jar","primary":false,"hashes":{"sha512":"aaa"}},
        {"filename":"mod.jar","url":"https://example.org/m.jar","primary":true,"hashes":{"sha512":"bbb"}}]}]
        """))));
        var version = Assert.Single(await api.GetVersionsAsync("p1", "1.21.1", "mod", "fabric"));
        Assert.Equal("mod.jar", version.FileName); Assert.Equal("bbb", version.Sha512);
        Assert.Equal("dep", Assert.Single(version.Dependencies).ProjectId);
    }
    [Fact]
    public async Task IncompatibleVersionsAreRejectedEvenIfServerReturnsThem()
    {
        var api = new ModrinthApi(new HttpClient(new FakeHttp(_ => Json("""
        [{"id":"v1","project_id":"p1","version_number":"1","game_versions":["1.20"],"loaders":["forge"],"files":[]}]
        """))));
        Assert.Empty(await api.GetVersionsAsync("p1", "1.21.1", "mod", "fabric"));
    }
    [Theory]
    [InlineData("../escape.jar")][InlineData("mods/../../escape.jar")][InlineData("C:\\evil.jar")]
    [InlineData("/root.jar")][InlineData("mods/CON.jar")][InlineData("mods/a.jar:stream")]
    public void UnsafePathsRejected(string path) => Assert.Throws<InvalidDataException>(() => VerifiedDownload.ContainedPath(Path.GetTempPath(), path));
    [Fact]
    public async Task CorruptDownloadDoesNotReplaceExistingFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "mod.jar"); await File.WriteAllTextAsync(path, "original");
            var http = new HttpClient(new FakeHttp(_ => Json("corrupt")));
            await Assert.ThrowsAsync<InvalidDataException>(() => VerifiedDownload.FetchAsync(http, ["https://example.org/file"], path, new string('0',128), null));
            Assert.Equal("original", await File.ReadAllTextAsync(path)); Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task DownloadFallsBackAndValidatesHash()
    {
        var data = Encoding.UTF8.GetBytes("valid"); var hash = Convert.ToHexString(SHA512.HashData(data));
        var http = new HttpClient(new FakeHttp(r => r.RequestUri!.AbsolutePath == "/bad" ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".jar");
        try { await VerifiedDownload.FetchAsync(http, ["https://example.org/bad", "https://example.org/good"], path, hash, null); Assert.Equal(data, await File.ReadAllBytesAsync(path)); }
        finally { File.Delete(path); }
    }
}

public class AdditionalDownloadTests
{
    [Fact]
    public async Task CancellationLeavesNoPartialFile()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        try
        {
            using var cts=new CancellationTokenSource();cts.Cancel();
            var path=Path.Combine(dir,"mod.jar");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>VerifiedDownload.FetchAsync(new HttpClient(),["https://example.org/file"],path,new string('0',128),null,cts.Token));
            Assert.Empty(Directory.GetFiles(dir));
        }finally{Directory.Delete(dir,true);}
    }
    [Fact]
    public void SafePathAcceptsRootWithTrailingSeparator()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(),"mods","test.jar"),VerifiedDownload.ContainedPath(Path.GetTempPath(),"mods/test.jar"));
    }
}

public class StreamingCancellationTests
{
    [Fact]
    public async Task CancellationDuringStreamingPreservesDestinationAndRemovesPartialFile()
    {
        var dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(dir);
        using var cts=new CancellationTokenSource();
        var stream=new BlockingStream();
        var http=new HttpClient(new ModrinthTests.FakeHttp(_=>new(HttpStatusCode.OK){Content=new StreamContent(stream)}));
        var path=Path.Combine(dir,"mod.jar");await File.WriteAllTextAsync(path,"original");
        try
        {
            var download=VerifiedDownload.FetchAsync(http,["https://example.org/file"],path,new string('0',128),null,cts.Token);
            await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2,Directory.GetFiles(dir).Length);
            cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>download);
            Assert.Equal("original",await File.ReadAllTextAsync(path));Assert.Single(Directory.GetFiles(dir));
        }finally{Directory.Delete(dir,true);}
    }
    private sealed class BlockingStream:Stream
    {
        private bool _first=true;
        public TaskCompletionSource Waiting{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {
            if(_first){_first=false;buffer.Span[0]=1;return 1;}
            Waiting.TrySetResult();await Task.Delay(Timeout.Infinite,cancellationToken);return 0;
        }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
