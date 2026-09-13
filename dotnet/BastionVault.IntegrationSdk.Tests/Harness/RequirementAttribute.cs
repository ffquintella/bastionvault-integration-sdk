namespace BastionVault.IntegrationSdk.Tests.Harness;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirementAttribute : Attribute
{
    public RequirementAttribute(string id)
    {
        Id = id;
    }

    public string Id { get; }
}
