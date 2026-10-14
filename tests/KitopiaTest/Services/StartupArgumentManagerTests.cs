using Microsoft.VisualStudio.TestTools.UnitTesting;
using PluginCore;

namespace KitopiaTest.Services;

[TestClass]
public sealed class StartupArgumentManagerTests
{
    [TestMethod]
    public void Parse_ScenarioVersionUrl_PreservesSelectedVersion()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://action=DownloadScenario;value=42;version=2.0.0-beta.1"]);
        Assert.AreEqual(StartupAction.DownloadScenario, result.Action);
        Assert.AreEqual("42", result.Value);
        Assert.AreEqual("2.0.0-beta.1", result.Extras["version"]);
    }

    [TestMethod]
    public void Parse_SemicolonLoginUrl_ReturnsLoginActionAndToken()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://action=Login;token=test_token_123"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("test_token_123", result.Value);
        Assert.AreEqual("test_token_123", result.Extras["token"]);
    }

    [TestMethod]
    public void Parse_QueryStringLoginUrl_ReturnsLoginActionAndToken()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://login?token=my_secret_token_456"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("my_secret_token_456", result.Value);
        Assert.AreEqual("my_secret_token_456", result.Extras["token"]);
    }

    [TestMethod]
    public void Parse_TokenOnlyUrl_InfersLoginAction()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://token=abc789"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("abc789", result.Value);
    }

    [TestMethod]
    public void Parse_SemicolonOAuthUrl_ReturnsLoginActionCodeAndState()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://action=Login;code=oauth_code_789;state=csrf_state_xyz"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("oauth_code_789", result.Value);
        Assert.AreEqual("oauth_code_789", result.Extras["code"]);
        Assert.AreEqual("csrf_state_xyz", result.Extras["state"]);
    }

    [TestMethod]
    public void Parse_TripleSlashOAuthUrl_ReturnsLoginActionCodeAndState()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl:///action=Login&code=oauth_code_abc&state=csrf_state_123"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("oauth_code_abc", result.Value);
        Assert.AreEqual("oauth_code_abc", result.Extras["code"]);
        Assert.AreEqual("csrf_state_123", result.Extras["state"]);
    }

    [TestMethod]
    public void Parse_ActionPathWithQueryParams_ReturnsLoginActionCodeAndState()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://action=Login?code=oauth_code_xyz&state=csrf_state_999"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("oauth_code_xyz", result.Value);
        Assert.AreEqual("oauth_code_xyz", result.Extras["code"]);
        Assert.AreEqual("csrf_state_999", result.Extras["state"]);
    }

    [TestMethod]
    public void Parse_SemicolonOAuthUrl_WithTrailingSlash_ReturnsCleanCodeAndState()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://action=Login;code=oauth_code_789;state=csrf_state_xyz/"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("oauth_code_789", result.Value);
        Assert.AreEqual("oauth_code_789", result.Extras["code"]);
        Assert.AreEqual("csrf_state_xyz", result.Extras["state"]);
    }

    [TestMethod]
    public void Parse_QueryOAuthUrl_WithTrailingSlash_ReturnsCleanCodeAndState()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://action=Login?code=oauth_code_xyz&state=csrf_state_999/"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("oauth_code_xyz", result.Value);
        Assert.AreEqual("oauth_code_xyz", result.Extras["code"]);
        Assert.AreEqual("csrf_state_999", result.Extras["state"]);
    }

    [TestMethod]
    public void Parse_TripleSlashOAuthUrl_WithTrailingSlash_ReturnsCleanCodeAndState()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl:///action=Login&code=oauth_code_abc&state=csrf_state_123/"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
        Assert.AreEqual("oauth_code_abc", result.Value);
        Assert.AreEqual("oauth_code_abc", result.Extras["code"]);
        Assert.AreEqual("csrf_state_123", result.Extras["state"]);
    }

    [TestMethod]
    public void Parse_ActionWithTrailingSlash_ReturnsLoginAction()
    {
        var result = StartupArgumentManager.Parse(["kitopiaurl://action=Login/"]);

        Assert.AreEqual(StartupAction.Login, result.Action);
    }
}
