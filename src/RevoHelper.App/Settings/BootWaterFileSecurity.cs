using System.Security.AccessControl;
using System.Security.Principal;

namespace RevoHelper.App.Settings;

public static class BootWaterFileSecurity
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemAccount = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    public static DirectorySecurity CreateDirectorySecurity()
    {
        var acl = new DirectorySecurity();
        Configure(acl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
        return acl;
    }

    public static FileSecurity CreateFileSecurity()
    {
        var acl = new FileSecurity(); Configure(acl, InheritanceFlags.None); return acl;
    }

    private static void Configure(FileSystemSecurity acl, InheritanceFlags inheritance)
    {
        acl.SetOwner(Administrators);
        acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { Administrators, SystemAccount })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
    }

    public static bool IsProtected(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (owner != Administrators && owner != SystemAccount) return false;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        const FileSystemRights readOnly = FileSystemRights.ReadAndExecute | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;
        return rules.Any(rule => rule.IdentityReference == SystemAccount && rule.AccessControlType == AccessControlType.Allow &&
                                 (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl) &&
            rules.All(rule => rule.AccessControlType != AccessControlType.Allow || rule.IdentityReference == Administrators ||
                rule.IdentityReference == SystemAccount || (rule.FileSystemRights & ~readOnly) == 0);
    }
}
