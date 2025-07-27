using Refit;

namespace Aspire.Hosting.MinIo.Clients;

internal interface IMinioAdminClient
{
    [Post("/api/v1/login")]
    public Task<IApiResponse> Login(LoginRequest request);

    [Get("/api/v1/policy/{policyName}")]
    public Task<IApiResponse> GetPolicy(string policyName);

    [Post("/api/v1/policies")]
    public Task<IApiResponse> CreatePolicy(CreatePolicy request);
}

internal record LoginRequest(string Accesskey, string Secretkey);

internal record CreatePolicy(string Name, string Policy);