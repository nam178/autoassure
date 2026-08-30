namespace A2.Server.Common;

public static class DesignTimeBuild
{
    ///<summary>
    /// During `dotnet build`, the OpenAPI SDK generator (Microsoft.Extensions.ApiDescription.Server)
    /// boots this app in-process under an entry assembly named "GetDocument.Insider" just to inspect
    /// routes, without ASPNETCORE_ENVIRONMENT set or AWS access available. Startup code that talks to
    /// AWS (SSM, secret validation, etc.) must skip itself in that case or the build fails.
    /// </summary>
    public static bool IsActive =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name != "A2.Server";
}
