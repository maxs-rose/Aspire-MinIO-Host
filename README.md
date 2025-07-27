# Aspire MinIO Hosting Integration

A simple MinIO hosting integration to allow for the creation of buckets, policies, and users

> [!IMPORTANT]  
> Due to changes it MinIO the ability to create policies and users has been remived from the community edition.
> 
> The integraiton by default will use the `latest` image provided by MinIO, to return to the most recent image with 
> the ability to create users and policies set `WithImageTag` to `MinIoContainerImageTag.AdminUi`
> 
> Example:
> ```csharp
> builder.AddMinIo("tenant")
>    .WithImageTag(MinIoContainerImageTag.AdminUi)
> ```

# Usage

## Adding MinIO

```csharp
builder.AddMinIo("tenant");
```

Parameters:
- `tenant` - The tenant name
- `username` - (Optional) The admin username for the tenant
- `password` - (Optional) The admin password for the tenant

Extension methods:
- `WithDataVolume`

## Adding a Bucket

```csharp
var tenant = builder.AddMinIo("tenant");

var bucket = tenant.AddBucket("exampleBucket");
```

Parameters:
- `name` - The bucket name 

Extension methods:
- `WithPolicy`
    - Can be either `Public`, `Private`, or a custom policy

## Adding a Policy

```csharp
var tenant = builder.AddMinIo("tenant");

var policy = tenant.AddPolicy("examplePolicy", "{ ..policy json.. }");
```

Parameters:
- `name` - Policy name
- `policyJson` - JSON string of the policy

## Adding a User

```csharp
var tenant = builder.AddMinIo("tenant");

var user = tenant.AddUser("exampleUser");
```

Parameters:
- `name` - Username for the user
- `secretAccessKey` - (Optional) Secret Access Key for the user


Extension methods:
- `WithPolicy` - Referce a policy created through `AddPolicy`