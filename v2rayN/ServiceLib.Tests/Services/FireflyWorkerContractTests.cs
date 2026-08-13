using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Services;

public class FireflyWorkerContractTests
{
    [Fact]
    public void ConstItem_ShouldDefaultToTheFireflyClientEndpoint()
    {
        new ConstItem().FireflyApiUrl.Should().Be(Global.FireflyClientApiUrl);
    }

    [Fact]
    public void ClientResponse_ShouldDeserializeTheWorkerContract()
    {
        const string json = """
        {
          "subscriptions": [{"id":"1","name":"主线路","enabled":true,"encrypted":true,"url":"https://worker.example/api/subscription/1"}],
          "notice": {"hasNotice":true,"title":"维护通知","content":"<b>今晚维护</b>","noticeId":"notice_1","showOnce":true},
          "appUpdate": {"version":"1.2.0","versionCode":2,"downloadUrl":"https://downloads.example/FireflyVPN.zip","changelog":"修复问题","is_force":1},
          "appUpdate_pc": {"version":"1.2.0-pc","versionCode":3,"downloadUrl":"https://downloads.example/FireflyVPN-win-x64.zip","changelog":"桌面端修复","is_force":0},
          "settings": {"websiteUrl":"https://firefly.example","nodeRequestTimeoutMs":25000}
        }
        """;

        var state = JsonUtils.Deserialize<FireflyClientState>(json);

        state.Should().NotBeNull();
        state!.Subscriptions.Should().ContainSingle();
        state.Subscriptions![0].Url.Should().Be("https://worker.example/api/subscription/1");
        state.Notice!.NoticeId.Should().Be("notice_1");
        state.AppUpdate!.VersionCode.Should().Be(2);
        state.AppUpdate.IsForce.Should().Be(1);
        state.AppUpdatePc!.VersionCode.Should().Be(3);
        state.Settings!.NodeRequestTimeoutMs.Should().Be(25000);
    }
}
