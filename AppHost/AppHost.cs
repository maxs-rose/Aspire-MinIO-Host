using Aspire.Hosting.MinIo;
using Aspire.Hosting.MinIo.Models;

var builder = DistributedApplication.CreateBuilder(args);

var tenant = builder.AddMinIo("minio")
    .WithImageTag(MinIoContainerImageTag.AdminUi);

tenant.AddBucket("some-bucket").WithPolicy(BucketPolicy.Public);

var policy = tenant.AddPolicy(
    "testpolicy",
    "{\n    \"Version\": \"2012-10-17\",\n    \"Statement\": [\n        {\n            \"Effect\": \"Allow\",\n            \"Action\": [\n                \"s3:PutObject\"\n            ],\n            \"Resource\": [\n                \"arn:aws:s3:::*\"\n            ]\n        }\n    ]\n}");

var user = tenant.AddUser("testuser", builder.AddParameter("accessKey", () => "sometestkey"))
    .WithPolicy(policy);

builder.Build().Run();