using System.Reflection;
using IdentityServerProject.Services.Users;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
/// Guards the extracted-library invariant behind every persistence port in
/// <c>IdentityServerProject.Admin.Services</c>: the library must never depend on the web host or
/// on <c>IdentityServerProject.Data</c>. A reference either way would silently reintroduce the host
/// coupling the ports exist to remove, defeating the whole extraction.
/// </summary>
public class AdminServicesDependencyDirectionTests
{
    private static readonly Assembly AdminServicesAssembly = typeof(IIdentityUserAdministrationStore).Assembly;

    [Fact]
    public void AdminServicesAssembly_DoesNotReferenceTheWebHostAssembly()
    {
        var referencedAssemblyNames = AdminServicesAssembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain("IdentityServerProject", referencedAssemblyNames);
    }

    [Fact]
    public void AdminServicesAssembly_DefinesNoTypeInTheHostDataNamespace()
    {
        var dataNamespaceTypes = AdminServicesAssembly.GetTypes()
            .Where(t => t.Namespace != null && t.Namespace.StartsWith("IdentityServerProject.Data", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(dataNamespaceTypes);
    }
}
