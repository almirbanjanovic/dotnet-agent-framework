var builder = DistributedApplication.CreateBuilder(args);

var crmApi = builder.AddProject<Projects.Contoso_CrmApi>("crm-api")
    .WithHttpEndpoint(port: 5001, name: "http");

var crmMcp = builder.AddProject<Projects.Contoso_CrmMcp>("crm-mcp")
    .WithHttpEndpoint(port: 5002, name: "http")
    .WithReference(crmApi);

var knowledgeMcp = builder.AddProject<Projects.Contoso_KnowledgeMcp>("knowledge-mcp")
    .WithHttpEndpoint(port: 5003, name: "http");

var crmAgent = builder.AddProject<Projects.Contoso_CrmAgent>("crm-agent")
    .WithHttpEndpoint(port: 5004, name: "http")
    .WithReference(crmMcp)
    .WithReference(knowledgeMcp);

var productAgent = builder.AddProject<Projects.Contoso_ProductAgent>("product-agent")
    .WithHttpEndpoint(port: 5005, name: "http")
    .WithReference(crmMcp)
    .WithReference(knowledgeMcp);

var orchestratorAgent = builder.AddProject<Projects.Contoso_OrchestratorAgent>("orchestrator-agent")
    .WithHttpEndpoint(port: 5006, name: "http")
    .WithReference(crmAgent)
    .WithReference(productAgent);

var bffApi = builder.AddProject<Projects.Contoso_BffApi>("bff-api")
    .WithHttpEndpoint(port: 5007, name: "http")
    .WithReference(crmApi)
    .WithReference(orchestratorAgent);

builder.AddProject<Projects.Contoso_BlazorUi>("blazor-ui")
    .WithHttpEndpoint(port: 5008, name: "http")
    .WithReference(bffApi);

builder.Build().Run();
