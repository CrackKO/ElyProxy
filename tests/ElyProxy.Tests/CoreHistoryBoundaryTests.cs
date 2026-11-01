using ElyProxy.Core;
using ElyProxy.Models;
using ElyProxy.Services;
using Xunit;
namespace ElyProxy.Tests;
public class CoreHistoryBoundaryTests
{
    [Fact] public void BoundsAndExpiry()
    {
        var path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try {
            var history = new CorePerformanceHistoryService(path);
            Assert.Null(history.Get("absent"));
            history.Record("old",true,1,now:DateTimeOffset.UtcNow.AddDays(-91));
            Assert.Null(history.Get("old"));
            for(int i=0;i<514;i++) history.Record(i.ToString(),true,1);
            var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(512,json.RootElement.EnumerateObject().Count());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,Path.GetFileName(path)+".*.tmp"));
        } finally { File.Delete(path); }
    }
    [Fact] public void RecentFailuresDecayAndStartupMatters()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try {
            var history=new CorePerformanceHistoryService(path); var node=new VlessNode(); var now=DateTimeOffset.UtcNow;
            string key=CorePerformanceHistoryService.Fingerprint(node,"Proxy","SingBox");
            history.Record(key,false,100,now:now);
            int Score(DateTimeOffset time) => CoreCandidateSelector.Evaluate(node,ProxyCoreKind.SingBox,true,true,history:history,now:time)[0].Score;
            Assert.True(Score(now)<Score(now.AddDays(10)));
            history.Record(key,true,100); int fast=Score(now);
            history.Record(key,true,20000); Assert.True(Score(now)<fast+5);
        } finally { File.Delete(path); }
    }
}
