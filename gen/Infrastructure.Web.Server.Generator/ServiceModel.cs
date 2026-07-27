namespace Norse.Infrastructure.Web.Server.Generator;

/// <summary>A discovered Norse gRPC contract with its resolved implementation, both global-qualified.</summary>
sealed record ServiceModel(string InterfaceName, string ImplementationTypeName);
