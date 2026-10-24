using System.Security.AccessControl;
using System.Security.Principal;
using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

public sealed class BootWaterFileSecurityTests
{
    [Fact]
    public void ProtectedDirectoryAndFilePoliciesAreAccepted()
    {
        Assert.True(BootWaterFileSecurity.IsProtected(BootWaterFileSecurity.CreateDirectorySecurity()));
        Assert.True(BootWaterFileSecurity.IsProtected(BootWaterFileSecurity.CreateFileSecurity()));
    }

    [Theory]
    [InlineData(FileSystemRights.Write)]
    [InlineData(FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [InlineData(FileSystemRights.ChangePermissions)]
    public void UserWritablePolicyIsRejected(FileSystemRights rights)
    {
        var acl = BootWaterFileSecurity.CreateDirectorySecurity();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), rights, AccessControlType.Allow));
        Assert.False(BootWaterFileSecurity.IsProtected(acl));
    }

    [Fact]
    public void UserOwnedPolicyIsRejectedEvenWhenReadOnly()
    {
        var acl = BootWaterFileSecurity.CreateFileSecurity();
        acl.SetOwner(new SecurityIdentifier("S-1-5-21-111-222-333-1001"));
        Assert.False(BootWaterFileSecurity.IsProtected(acl));
    }
}
