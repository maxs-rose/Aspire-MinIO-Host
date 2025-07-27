using Aspire.Hosting.S3;
using Aspire.Hosting.S3.Models;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddMinIo("minio")
    .AddBucket("some-bucket")
    .WithPolicy(BucketPolicy.Public);

builder.Build().Run();