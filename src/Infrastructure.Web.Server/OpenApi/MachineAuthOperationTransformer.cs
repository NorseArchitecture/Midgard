using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Norse.Abstractions.Web.Server.Facade;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
///     Attaches the bearer security requirement to an operation only when its declaring controller
///     derives from <see cref="GrpcControllerBase" /> (Himinbjorg#49) — the same construction that
///     protects the controller at runtime (Asgard's class-level <c>[Authorize(Policy = Machine)]</c>)
///     drives what the document tells partners about it. A document transformer cannot make this
///     distinction on its own; this is why the scheme registration (<see cref="BearerSecuritySchemeTransformer" />)
///     and the per-operation requirement are two separate transformers, mirroring
///     <c>StandardResponsesTransformer</c>'s own operation-level access to controller metadata.
/// </summary>
public sealed class MachineAuthOperationTransformer : IOpenApiOperationTransformer
{
	/// <inheritdoc />
	public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
		CancellationToken cancellationToken)
	{
		if (context.Description.ActionDescriptor is not ControllerActionDescriptor descriptor ||
			!typeof(GrpcControllerBase).IsAssignableFrom(descriptor.ControllerTypeInfo))
			return Task.CompletedTask;

		operation.Security ??= [];
		operation.Security.Add(new OpenApiSecurityRequirement
		{
			[new OpenApiSecuritySchemeReference(BearerSecuritySchemeTransformer.SchemeId, context.Document)] = []
		});

		return Task.CompletedTask;
	}
}
