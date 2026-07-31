using System;
using System.Linq;
using System.Text;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitHub;
using Octokit;
using Serilog;

partial class Build
{
    Target CreateRelease => _ => _
        .OnlyWhenDynamic(ShouldCreateRelease)
        .Executes(() =>
        {
            var repository = GitRepository.FromUrl("https://github.com/Maklith/Kitopia");
            var version = AvaloniaProject.GetProperty("Version");
            var body = BuildReleaseNotes(repository);

            GitHubClient.Git.Reference.Create(
                repository.GetGitHubOwner(),
                repository.GetGitHubName(),
                new NewReference($"refs/tags/{version}", GitTasks.GitCurrentCommit())).Wait();

            Release = GitHubClient.Repository.Release.Create(
                repository.GetGitHubOwner(),
                repository.GetGitHubName(),
                new NewRelease(version)
                {
                    Name = version,
                    Prerelease = true,
                    Draft = false,
                    Body = body
                }).Result;
        });

    bool ShouldCreateRelease()
    {
        if (!IsRelease)
            throw new InvalidOperationException(
                "GitHubToken is required because every package build must create a new GitHub release.");

        GitHubClient = new GitHubClient(new ProductHeaderValue("Kitopia"))
        {
            Credentials = new Credentials(GitHubToken)
        };
        var repository = GitRepository.FromUrl("https://github.com/Maklith/Kitopia");
        var version = AvaloniaProject.GetProperty("Version");
        var tags = GitHubClient.Repository.GetAllTags(repository.GetGitHubOwner(), repository.GetGitHubName()).Result;
        if (tags.All(tag => tag.Name != version))
            return true;

        Log.Information("Skipping package build because GitHub tag {Version} already exists", version);
        return false;
    }

    string BuildReleaseNotes(GitRepository repository)
    {
        var body = new StringBuilder();
        var tags = GitHubClient.Repository.GetAllTags(repository.GetGitHubOwner(), repository.GetGitHubName()).Result;
        if (tags.Count == 0)
            return "无明确更新说明";

        var commit = GitTasks.GitCurrentCommit();
        var previousCommit = tags.First().Commit.Sha;
        for (var depth = 0; commit != previousCommit && depth < 50; depth++)
        {
            var gitHubCommit = GitHubClient.Repository.Commit.Get(
                repository.GetGitHubOwner(), repository.GetGitHubName(), commit).Result;
            if (gitHubCommit.Commit.Message.Length >= 3 && !gitHubCommit.Commit.Message.StartsWith('*'))
                body.AppendLine(gitHubCommit.Commit.Message);
            if (gitHubCommit.Parents.Count == 0)
                break;
            commit = gitHubCommit.Parents.First().Sha;
        }

        return body.Length == 0 ? "无明确更新说明" : body.ToString();
    }
}
