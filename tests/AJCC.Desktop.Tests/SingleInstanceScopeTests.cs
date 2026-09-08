using System.Reflection;
using AJCC.Desktop.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Desktop.Tests;

[TestClass]
public sealed class SingleInstanceScopeTests
{
    [TestMethod]
    public void UserScopedIpcName_IsPrivateDeterministicAndSeparatesUsers()
    {
        Type serviceType = typeof(CoreProfileStore).Assembly.GetType(
            "AJCC.Desktop.Services.AjSingleInstanceService",
            throwOnError: true)!;
        MethodInfo method = serviceType.GetMethod(
            "BuildUserScopedName",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(string), typeof(string), typeof(string) },
            modifiers: null)!;

        const string baseName = "AJCC.Instance";
        string first = Invoke(method, baseName, "alice", "/home/alice", "windows-standard");
        string same = Invoke(method, baseName, "alice", "/home/alice", "windows-standard");
        string otherUser = Invoke(method, baseName, "bob", "/home/bob", "windows-standard");
        string otherScope = Invoke(method, baseName, "alice", "/srv/alice", "windows-standard");
        string otherSecurityScope =
            Invoke(method, baseName, "alice", "/home/alice", "windows-privileged");

        Assert.AreEqual(first, same);
        Assert.AreNotEqual(first, otherUser);
        Assert.AreNotEqual(first, otherScope);
        Assert.AreNotEqual(first, otherSecurityScope);
        Assert.IsTrue(first.StartsWith(baseName + ".", StringComparison.Ordinal));
        Assert.IsFalse(first.Contains("alice", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(first.Contains("/home/alice", StringComparison.Ordinal));
        string token = first[(baseName.Length + 1)..];
        Assert.AreEqual(24, token.Length);
        Assert.IsTrue(token.All(Uri.IsHexDigit));
    }

    private static string Invoke(
        MethodInfo method,
        string baseName,
        string userName,
        string userScopeRoot,
        string securityScope)
        => (string)method.Invoke(
            null,
            new object?[] { baseName, userName, userScopeRoot, securityScope })!;
}
