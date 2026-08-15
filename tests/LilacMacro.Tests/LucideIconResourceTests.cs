using System.Text.RegularExpressions;

namespace LilacMacro.Tests;

public sealed partial class LucideIconResourceTests
{
    [Fact]
    public void EveryReferencedLucideIconExistsInTheSharedDictionary()
    {
        string repository = RepositoryRoot();
        string appRoot = Path.Combine(repository, "src", "LilacMacro.App");
        string dictionary = File.ReadAllText(Path.Combine(appRoot, "Themes", "LucideIcons.xaml"));
        HashSet<string> defined = IconKeyPattern()
            .Matches(dictionary)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        string[] referenced = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => IconReferencePattern().Matches(File.ReadAllText(path)))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(referenced);
        Assert.All(referenced, key => Assert.Contains(key, defined));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "runtime-evidence.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the LilacMacro repository root.");
    }

    [GeneratedRegex("x:Key=\"(Lucide\\.[A-Za-z0-9_]+)\"")]
    private static partial Regex IconKeyPattern();

    [GeneratedRegex("StaticResource[ \\t]+(Lucide\\.[A-Za-z0-9_]+)")]
    private static partial Regex IconReferencePattern();
}
