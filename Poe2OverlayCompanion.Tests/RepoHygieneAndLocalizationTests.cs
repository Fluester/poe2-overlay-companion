using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Poe2OverlayCompanion.Tests;

public sealed class RepoHygieneAndLocalizationTests
{
    private static string FindRepoRoot([CallerFilePath] string callerPath = "")
    {
        DirectoryInfo? dir = new DirectoryInfo(Path.GetDirectoryName(callerPath)!);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Poe2OverlayCompanion.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Poe2OverlayCompanion.csproj repo root.");
    }

    [Fact]
    public void XamlFiles_HaveNoUnnamedCyrillicStringsOutsideCodeBehind()
    {
        string root = FindRepoRoot();
        var xamlFiles = Directory.GetFiles(root, "*.xaml", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(xamlFiles);

        foreach (string xamlPath in xamlFiles)
        {
            string baseName = Path.GetFileNameWithoutExtension(xamlPath);
            var codeBehindFiles = Directory.GetFiles(root, $"{baseName}*.cs", SearchOption.TopDirectoryOnly);
            string codeBehind = string.Join("\n", codeBehindFiles.Select(p => File.ReadAllText(p, Encoding.UTF8)));

            string raw = File.ReadAllText(xamlPath, Encoding.UTF8);
            string noComments = Regex.Replace(raw, @"<!--[\s\S]*?-->", "");
            var tags = Regex.Matches(noComments, @"<[A-Za-z0-9_.:]+\b[^>]*?(?:Text|Content|Header|ToolTip|Title)=""[^""]*[\u0400-\u04FF][^""]*""[^>]*>");

            foreach (Match t in tags)
            {
                var nameMatch = Regex.Match(t.Value, @"x:Name=""([^""]+)""");
                Assert.True(nameMatch.Success, $"Unnamed Cyrillic element in {Path.GetFileName(xamlPath)}: {t.Value}");

                string controlName = nameMatch.Groups[1].Value;
                Assert.True(
                    Regex.IsMatch(codeBehind, $@"\b{Regex.Escape(controlName)}\b"),
                    $"Cyrillic element '{controlName}' in {Path.GetFileName(xamlPath)} is not referenced/localized in code-behind.");
            }
        }
    }

    [Fact]
    public void RepoFiles_ContainNoLeakedSecretsOrPersonalPaths()
    {
        string root = FindRepoRoot();
        string[] exts = [".cs", ".xaml", ".yml", ".json", ".iss", ".md"];
        string[] excludedDirs = [
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Poe2OverlayCompanion.Tests{Path.DirectorySeparatorChar}"
        ];

        var secretPatterns = new Regex[]
        {
            new(@"AIza[0-9A-Za-z\-_]{20,}", RegexOptions.Compiled),
            new(@"sk-[A-Za-z0-9]{16,}", RegexOptions.Compiled),
            new(@"POESESSID\s*=\s*[0-9a-f]{16,}", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"C:\\Users\\[^""\s\\]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => exts.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .Where(p => !excludedDirs.Any(ex => p.Contains(ex, StringComparison.OrdinalIgnoreCase)));

        foreach (string file in files)
        {
            string txt = File.ReadAllText(file, Encoding.UTF8);
            foreach (var rx in secretPatterns)
            {
                var m = rx.Match(txt);
                Assert.False(m.Success, $"Suspicious pattern '{m.Value}' found in {Path.GetRelativePath(root, file)}");
            }
        }
    }
}
