using Refit;

namespace Aspire.Hosting.MinIo.Clients;

internal interface IMinioAdminClient
{
    HttpClient Client { get; }

    [Post("/api/v1/login")]
    public Task<IApiResponse> Login(LoginRequest request);

    [Get("/api/v1/policy/{policyName}")]
    public Task<IApiResponse> GetPolicy(string policyName);

    [Post("/api/v1/policies")]
    public Task<IApiResponse> CreatePolicy(CreatePolicy request);

    [Get("/api/v1/user/{username}")]
    public Task<IApiResponse> GetUser(string username);

    [Post("/api/v1/users")]
    public Task<IApiResponse> CreateUser(CreateUser request);
}

internal record LoginRequest(string Accesskey, string Secretkey);

internal record CreatePolicy(string Name, string Policy);

internal record CreateUser(string AccessKey, string SecretKey, string[] Policies)
{
    public string[] Groups { get; init; } = [];
}