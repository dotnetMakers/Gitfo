﻿using CommandLine;
using Gitfo;
using System.Diagnostics;
using System.Text.Json;

const int NameWidth = 30;
const int PropertyWidth = 6;

Console.ForegroundColor = ConsoleColor.White;
Console.BackgroundColor = ConsoleColor.Black;

//update to add -f --fetch as a command line param
//update to add -C --directory as a param
//update to add -c --checkout 
//update to add -p --pull
//update ot add -v --version
var rootPath = Environment.CurrentDirectory;

Console.WriteLine("| Gitfo v0.3.0");
Console.WriteLine("|");

string? profileName = null;

Parser.Default.ParseArguments<BaseOptions>(args)
    .MapResult(
                (BaseOptions opts) =>
                {
                    profileName = opts.ProfileName ?? "main";
                    return 0;
                },
                _ => 1);

var loadResult = LoadOptions(rootPath);

if (loadResult.result == 2)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write("Unable to load .gitfo config");
    Console.ForegroundColor = ConsoleColor.White;
    return loadResult.result;
}

var options = loadResult.options;

if (options == null && !args.Contains("generate"))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write("Unable to load .gitfo config");
    Console.ForegroundColor = ConsoleColor.White;
    return 1;
}

IEnumerable<Repo>? repos = null;
GitfoOptions.Profile? selectedProfile = null;

if (options != null)
{
    selectedProfile = options.Profiles.FirstOrDefault(p => p.Name == profileName);
    if (selectedProfile == null)
    {
        selectedProfile = options.Profiles.FirstOrDefault(p => p.Name.Contains("default"));
        if (selectedProfile == null)
        {
            selectedProfile = options.Profiles.First();
        }
    }
    repos = LoadRepos(rootPath, selectedProfile);
}

var reload = false;

var result = Parser.Default.ParseArguments<
    SyncOptions,
    PullOptions,
    FetchOptions,
    CheckoutOptions,
    GenerateOptions,
    StatusOptions>(args)
            .MapResult(
                (SyncOptions opts) =>
                {
                    reload = true;
                    return Sync(repos, opts);
                },
                (FetchOptions opts) => Fetch(repos, opts),
                (PullOptions opts) => Pull(repos, opts),
                (CheckoutOptions opts) => Checkout(repos, opts),
                (GenerateOptions opts) =>
                {
                    reload = true;
                    var gen = Generate(rootPath, opts);
                    options = gen.options;
                    return gen.result;
                },
                (StatusOptions opts) => 0,
                errs => 2);

Console.WriteLine("|");

if (reload)
{
    // TODO: find the one called "default" 
    selectedProfile = options.Profiles.First();
    repos = LoadRepos(rootPath, selectedProfile);
}

ShowGitfoTable(repos);

return result;

(int result, GitfoOptions? options) LoadOptions(string path)
{
    var directory = new DirectoryInfo(path);
    if (!directory.Exists)
    {
        if (Debugger.IsAttached)
        {
            throw new DirectoryNotFoundException();
        }
        return (1, null);
    }

    // look for a '.gitfo' file
    var optionPath = Path.Combine(directory.FullName, GitfoOptions.OptionsFileName);
    if (File.Exists(optionPath))
    {
        if (GitfoOptions.TryParse(File.ReadAllText(optionPath), out GitfoOptions options))
        {
            return (0, options);
        }
    }

    return (2, null);
}

IEnumerable<Repo> LoadRepos(string rootPath, GitfoOptions.Profile profile)
{
    var repos = new List<Repo>();

    foreach (var repo in profile.Repos)
    {
        var folder = Path.Combine(rootPath, repo.LocalFolder ?? repo.Owner, repo.RepoName);

        var r = new Repo(folder, repo);

        repos.Add(r);
    }

    return repos;
}

(int result, GitfoOptions? options) Generate(string rootPath, GenerateOptions options)
{
    var configPath = Path.Combine(rootPath, GitfoOptions.OptionsFileName);
    if (File.Exists(configPath))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("| .gitfo config already exists in target folder");
        Console.ForegroundColor = ConsoleColor.White;
        return (2, null);
    }

    var generatedRepos = new List<GitfoOptions.RepositoryInfo>();

    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("| Generating...");

    foreach (var owner in Directory.GetDirectories(rootPath))
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("| owner ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(Path.GetFileName(owner));
        Console.ForegroundColor = ConsoleColor.White;

        foreach (var candidate in Directory.GetDirectories(owner))
        {
            var test = Path.Combine(candidate, ".git");
            if (!Directory.Exists(test))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"|   skipping {Path.GetFileName(candidate)}");
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"|   adding   {Path.GetFileName(candidate)}");
                Console.ForegroundColor = ConsoleColor.White;

                generatedRepos.Add(new GitfoOptions.RepositoryInfo
                {
                    LocalFolder = Path.GetFileName(owner),
                    Owner = GitConfigParser.GetOwner(test),
                    RepoName = Path.GetFileName(candidate),
                    DefaultBranch = "main",
                });
            }
        }
    }

    var generatedOptions = new GitfoOptions();
    generatedOptions.Profiles.Add("default", generatedRepos);
    var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    File.WriteAllText(configPath, JsonSerializer.Serialize(generatedOptions, opts));
    return (0, generatedOptions);
}

int Pull(IEnumerable<Repo> repos, PullOptions options)
{
    foreach (var repo in repos)
    {
        Console.Write($"| Pull {repo.Name}...");

        if (repo.Pull())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("succeeded");
            Console.ForegroundColor = ConsoleColor.White;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("failed");
            Console.ForegroundColor = ConsoleColor.White;
        }

        Console.WriteLine();
    }

    return 0;
}

int Sync(IEnumerable<Repo> repos, SyncOptions options)
{
    foreach (var repo in repos)
    {
        Console.Write($"| Sync {repo.Name}...");

        if (repo.Sync())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("succeeded");
            Console.ForegroundColor = ConsoleColor.White;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("failed");
            Console.ForegroundColor = ConsoleColor.White;
        }

        Console.WriteLine();
    }

    return 0;
}

int Checkout(IEnumerable<Repo> repos, CheckoutOptions options)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.Write($"| Attempting to checkout ");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write(options.BranchName ?? "[default]");
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($" for all repos");

    foreach (var repo in repos)
    {
        Console.Write($"| Checkout ");

        var branch = options.BranchName ?? repo.DefaultBranch;

        if (repo.Checkout(branch))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("succeeded");
            Console.ForegroundColor = ConsoleColor.White;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("failed");
            Console.ForegroundColor = ConsoleColor.White;
        }

        Console.Write($" for ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(repo.Name);
        Console.ForegroundColor = ConsoleColor.White;
    }
    Console.WriteLine("|");

    return 0;
}

int Fetch(IEnumerable<Repo> repos, FetchOptions options)
{
    foreach (var repo in repos)
    {
        Console.Write($"| Fetch ");

        if (repo.Fetch())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("succeeded");
            Console.ForegroundColor = ConsoleColor.White;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("failed");
            Console.ForegroundColor = ConsoleColor.White;
        }

        Console.WriteLine($" for {repo.Name}");
    }
    Console.WriteLine("|");

    return 0;
}

void ShowGitfoTable(IEnumerable<Repo> repos)
{
    Console.WriteLine($"| {"Repo name".PadRight(NameWidth)} | {"Current branch".PadRight(NameWidth)} | {"Ahead".PadRight(PropertyWidth)} | {"Behind".PadRight(PropertyWidth)} | {"Dirty".PadRight(PropertyWidth)} |");
    Console.WriteLine($"| {"".PadRight(NameWidth, '-')} | {"".PadRight(NameWidth, '-')} | {"".PadRight(PropertyWidth, '-')} | {"".PadRight(PropertyWidth, '-')} | {"".PadRight(PropertyWidth, '-')} |");

    foreach (var repo in repos)
    {
        var name = repo.Name.PadRight(NameWidth);
        var friendly = repo.CurentBranch.PadRight(NameWidth);
        var ahead = $"{repo.Ahead}".PadRight(PropertyWidth);
        var behind = $"{repo.Behind}".PadRight(PropertyWidth);
        var dirty = $"{repo.IsDirty}".PadRight(PropertyWidth);

        Console.Write("| ");
        ConsoleWriteWithColor(name, ConsoleColor.White);

        ConsoleWriteWithColor(friendly, ahead[0] == ' ' ? ConsoleColor.Yellow : ConsoleColor.White);
        ConsoleWriteWithColor(ahead, ahead[0] == '0' ? ConsoleColor.White : ConsoleColor.Cyan);
        ConsoleWriteWithColor(behind, behind[0] == '0' ? ConsoleColor.White : ConsoleColor.Cyan);
        ConsoleWriteWithColor(dirty, repo.IsDirty ? ConsoleColor.Red : ConsoleColor.White);
        Console.WriteLine();
    }

    if (repos.Count() == 0)
    {
        Console.WriteLine("| No git repos found");
    }
}

void ConsoleWriteWithColor(string text, ConsoleColor color)
{
    if (text.Length > NameWidth)
    {
        text = string.Concat(text.AsSpan(0, NameWidth - 3), "...");
    }

    Console.ForegroundColor = color;
    Console.Write(text);
    Console.ForegroundColor = ConsoleColor.Gray;
    Console.Write(" | ");
}