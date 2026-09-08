using System.Reflection;
using AJCC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ArchitectureBoundaryTests
{
    [TestMethod]
    public void CoreAssembly_DoesNotReferenceDesktopFrameworks()
    {
        Assembly coreAssembly = typeof(AjState).Assembly;
        HashSet<string> references = coreAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] forbiddenReferences =
        {
            "PresentationFramework",
            "PresentationCore",
            "WindowsBase",
            "System.Xaml",
            "System.Windows.Forms"
        };

        foreach (string forbidden in forbiddenReferences)
            Assert.IsFalse(references.Contains(forbidden), $"AJCC.Core darf {forbidden} nicht referenzieren.");

        foreach (string reference in references)
        {
            Assert.IsFalse(
                reference.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase),
                $"AJCC.Core darf das Desktop-Framework {reference} nicht referenzieren.");
        }
    }

    [TestMethod]
    public void CoreAssembly_DoesNotExposeSystemWindowsTypes()
    {
        Assembly coreAssembly = typeof(AjState).Assembly;

        Type[] violatingTypes = coreAssembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.AreEqual(0, violatingTypes.Length, "AJCC.Core darf keine System.Windows-Typen enthalten.");
    }
}
