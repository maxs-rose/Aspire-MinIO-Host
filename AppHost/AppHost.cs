using Aspire.Hosting.MinIo;
using Aspire.Hosting.MinIo.Models;

var builder = DistributedApplication.CreateBuilder(args);

var tenant = builder.AddMinIo("minio");

tenant.AddBucket("some-bucket").WithPolicy(BucketPolicy.Public);

tenant.AddPolicy(
    "testpolicy",
    "{\n    \"Version\": \"2012-10-17\",\n    \"Statement\": [\n        {\n            \"Effect\": \"Allow\",\n            \"Action\": [\n                \"s3:PutObject\"\n            ],\n            \"Resource\": [\n                \"arn:aws:s3:::*\"\n            ]\n        }\n    ]\n}");

builder.Build().Run();