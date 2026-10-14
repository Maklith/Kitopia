using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Services.Account;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Account;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class AccountServiceTests
{
    [TestMethod]
    public void OpenBrowserLogin_RequestsScenarioWriteWithPkce()
    {
        var previousServices = ServiceManager.Services;
        var shell = new RecordingShell();
        using var provider = new ServiceCollection().AddSingleton<IDesktopShell>(shell).BuildServiceProvider();
        ServiceManager.Services = provider;
        try
        {
            new AccountService().OpenBrowserLogin();

            Assert.IsNotNull(shell.OpenedPath);
            var query = HttpUtility.ParseQueryString(new Uri(shell.OpenedPath).Query);
            CollectionAssert.Contains(query["scope"]!.Split(' '), "scenario:write");
            Assert.AreEqual("kitopia-desktop", query["client_id"]);
            Assert.AreEqual("S256", query["code_challenge_method"]);
            Assert.IsFalse(string.IsNullOrWhiteSpace(query["code_challenge"]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(query["state"]));
        }
        finally
        {
            ServiceManager.Services = previousServices;
        }
    }

    private sealed class RecordingShell : IDesktopShell
    {
        public string? OpenedPath { get; private set; }

        public void Open(string path, string? arguments = "", string? workingDirectory = "") => OpenedPath = path;

        public void RunAsAdmin(string path, string arguments = "") => throw new NotSupportedException();

        public void OpenFolderAndSelect(string path) => throw new NotSupportedException();
    }

    private sealed class FakeAccountService : IAccountService
    {
        public bool IsLoggedIn => CurrentUser != null && !string.IsNullOrWhiteSpace(CurrentToken);
        public UserInfo? CurrentUser { get; set; }
        public string? CurrentToken { get; set; }
        public event Action<UserInfo?>? UserStateChanged;

        public bool OpenBrowserLoginCalled { get; private set; }
        public bool LogoutCalled { get; private set; }
        public bool RefreshCalled { get; private set; }

        public Task<bool> LoginWithTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Task.FromResult(false);
            }

            CurrentToken = token;
            CurrentUser = new UserInfo
            {
                UserName = "test_user",
                Nickname = "测试开发人员",
                UserEmail = "test@kitopia.top",
                Roles = ["developer"]
            };
            UserStateChanged?.Invoke(CurrentUser);
            return Task.FromResult(true);
        }

        public Task<bool> ExchangeCodeAndLoginAsync(string code, string? state = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Task.FromResult(false);
            }
            return LoginWithTokenAsync($"token_from_{code}", cancellationToken);
        }

        public Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCalled = true;
            CurrentToken = null;
            CurrentUser = null;
            UserStateChanged?.Invoke(null);
            return Task.CompletedTask;
        }

        public Task<bool> RefreshUserInfoAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalled = true;
            return Task.FromResult(true);
        }

        public void OpenBrowserLogin()
        {
            OpenBrowserLoginCalled = true;
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task AccountCardViewModel_InitialLoggedOutState()
    {
        var fake = new FakeAccountService();
        using var vm = new AccountCardViewModel(fake);

        Assert.IsFalse(vm.IsLoggedIn);
        Assert.IsFalse(vm.IsLoggingIn);
        Assert.AreEqual("未登录", vm.DisplayName);
        Assert.AreEqual("U", vm.AvatarInitial);
    }

    [TestMethod]
    public async Task AccountCardViewModel_LoginCommand_TriggersBrowserOpenAndSetsLoggingIn()
    {
        var fake = new FakeAccountService();
        using var vm = new AccountCardViewModel(fake);

        vm.LoginCommand.Execute(null);

        Assert.IsTrue(fake.OpenBrowserLoginCalled);
        Assert.IsTrue(vm.IsLoggingIn);

        vm.CancelLoginCommand.Execute(null);
        Assert.IsFalse(vm.IsLoggingIn);
    }

    [TestMethod]
    public async Task AccountCardViewModel_LoginSucceeds_UpdatesProperties()
    {
        var fake = new FakeAccountService();
        using var vm = new AccountCardViewModel(fake);

        var success = await fake.LoginWithTokenAsync("test_valid_token");
        Assert.IsTrue(success);
        Assert.IsTrue(vm.IsLoggedIn);
        Assert.AreEqual("测试开发人员", vm.DisplayName);
        Assert.AreEqual("test_user", vm.UserName);
        Assert.AreEqual("test@kitopia.top", vm.Email);
        Assert.AreEqual("开发者", vm.PrimaryRole);
        Assert.AreEqual("测", vm.AvatarInitial);
    }

    [TestMethod]
    public async Task AccountCardViewModel_LogoutCommand_ResetsProperties()
    {
        var fake = new FakeAccountService();
        await fake.LoginWithTokenAsync("test_valid_token");

        using var vm = new AccountCardViewModel(fake);
        Assert.IsTrue(vm.IsLoggedIn);

        await vm.LogoutCommand.ExecuteAsync(null);

        Assert.IsTrue(fake.LogoutCalled);
        Assert.IsFalse(vm.IsLoggedIn);
        Assert.AreEqual("未登录", vm.DisplayName);
        Assert.AreEqual(string.Empty, vm.UserName);
    }

    [TestMethod]
    public void UserInfo_PrimaryRole_DeterminesCorrectRoleText()
    {
        var userAdmin = new UserInfo { Roles = ["superadmin", "user"] };
        Assert.AreEqual("管理员", userAdmin.PrimaryRole);

        var userDev = new UserInfo { Roles = ["developer"] };
        Assert.AreEqual("开发者", userDev.PrimaryRole);

        var userNormal = new UserInfo { Roles = ["user"] };
        Assert.AreEqual("用户", userNormal.PrimaryRole);

        var userEmpty = new UserInfo { Roles = [] };
        Assert.AreEqual("用户", userEmpty.PrimaryRole);
    }

    [TestMethod]
    public void AccountCardViewModel_ConstructorResolution_WithAccountService()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IAccountService, FakeAccountService>();
        services.AddSingleton<AccountCardViewModel>();
        using var provider = services.BuildServiceProvider();

        var vm = provider.GetService<AccountCardViewModel>();
        Assert.IsNotNull(vm);
        Assert.IsFalse(vm.IsLoggedIn);
    }

    private sealed class DelegatingTestHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; }

        public DelegatingTestHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            Handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    [TestMethod]
    public async Task RefreshUserInfoAsync_WhenServerReturns401_ClearsSessionAndNotifiesUserState()
    {
        var originalConfigs = ConfigManger.Configs;
        try
        {
            var config = new KitopiaConfig { userToken = "test_token" };
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = config
            };

            var userJson = """
            {
                "flag": true,
                "data": {
                    "userName": "test_user",
                    "nickname": "测试用户",
                    "userEmail": "test@kitopia.top",
                    "roles": ["user"]
                }
            }
            """;

            var handler = new DelegatingTestHandler(req =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(userJson, Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            var accountService = new AccountService(httpClient);

            var loginOk = await accountService.LoginWithTokenAsync("test_token");
            Assert.IsTrue(loginOk);
            Assert.IsTrue(accountService.IsLoggedIn);
            Assert.AreEqual("test_user", accountService.CurrentUser?.UserName);
            Assert.AreEqual("test_token", ConfigManger.Config.userToken);

            // Now configure handler to return 401 Unauthorized
            handler.Handler = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

            UserInfo? notifiedUser = null;
            var notifiedCount = 0;
            accountService.UserStateChanged += u =>
            {
                notifiedUser = u;
                notifiedCount++;
            };

            var refreshed = await accountService.RefreshUserInfoAsync();

            Assert.IsFalse(refreshed);
            Assert.IsFalse(accountService.IsLoggedIn);
            Assert.IsNull(accountService.CurrentUser);
            Assert.IsNull(accountService.CurrentToken);
            Assert.AreEqual(string.Empty, ConfigManger.Config.userToken);
            Assert.AreEqual(1, notifiedCount);
            Assert.IsNull(notifiedUser);
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
        }
    }

    [TestMethod]
    public async Task RefreshUserInfoAsync_WhenServerReturns200_UpdatesUserAndNotifiesUserState()
    {
        var originalConfigs = ConfigManger.Configs;
        try
        {
            var config = new KitopiaConfig { userToken = "test_token" };
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = config
            };

            var initialUserJson = """
            {
                "flag": true,
                "data": {
                    "userName": "test_user",
                    "nickname": "初始昵称",
                    "roles": ["user"]
                }
            }
            """;

            var updatedUserJson = """
            {
                "flag": true,
                "data": {
                    "userName": "test_user",
                    "nickname": "更新后昵称",
                    "roles": ["developer"]
                }
            }
            """;

            var isInitial = true;
            var handler = new DelegatingTestHandler(req =>
            {
                var content = isInitial ? initialUserJson : updatedUserJson;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            var accountService = new AccountService(httpClient);

            await accountService.LoginWithTokenAsync("test_token");
            Assert.AreEqual("初始昵称", accountService.CurrentUser?.Nickname);

            isInitial = false;
            var refreshed = await accountService.RefreshUserInfoAsync();

            Assert.IsTrue(refreshed);
            Assert.IsTrue(accountService.IsLoggedIn);
            Assert.AreEqual("更新后昵称", accountService.CurrentUser?.Nickname);
            Assert.AreEqual("开发者", accountService.CurrentUser?.PrimaryRole);
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
        }
    }

    [TestMethod]
    public async Task RefreshUserInfoAsync_WhenNetworkFails_PreservesSession()
    {
        var originalConfigs = ConfigManger.Configs;
        try
        {
            var config = new KitopiaConfig { userToken = "test_token" };
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = config
            };

            var userJson = """
            {
                "flag": true,
                "data": {
                    "userName": "test_user",
                    "nickname": "测试用户",
                    "roles": ["user"]
                }
            }
            """;

            var shouldFail = false;
            var handler = new DelegatingTestHandler(req =>
            {
                if (shouldFail)
                {
                    throw new HttpRequestException("Network failure");
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(userJson, Encoding.UTF8, "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            var accountService = new AccountService(httpClient);

            await accountService.LoginWithTokenAsync("test_token");
            Assert.IsTrue(accountService.IsLoggedIn);

            shouldFail = true;
            var refreshed = await accountService.RefreshUserInfoAsync();

            Assert.IsFalse(refreshed);
            Assert.IsTrue(accountService.IsLoggedIn);
            Assert.IsNotNull(accountService.CurrentUser);
            Assert.AreEqual("test_token", accountService.CurrentToken);
            Assert.AreEqual("test_token", ConfigManger.Config.userToken);
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
        }
    }
}
